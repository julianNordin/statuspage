using Microsoft.EntityFrameworkCore;
using StatusPage.Checker.Probing;
using StatusPage.Domain;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure;

namespace StatusPage.Checker;

/// <summary>What one pass over every component came to.</summary>
/// <param name="Probed">How many components were checked.</param>
/// <param name="Transitions">How many changed state, and so wrote a row.</param>
/// <param name="IncidentsOpened">How many outages the checker declared by itself.</param>
public sealed record CycleResult(int Probed, int Transitions, int IncidentsOpened);

/// <summary>
/// One pass: probe every enabled component, decide whether anything actually changed, and
/// write only what did.
/// <para>
/// Writing only transitions is the decision this whole project is shaped around. A row per
/// check would be roughly 130,000 rows per component per quarter and would keep a serverless
/// database awake permanently. A row per <em>change</em> is a handful, and the database
/// sleeps in between.
/// </para>
/// </summary>
public sealed partial class CheckCycle(
    StatusPageDbContext db,
    ITargetProbe probe,
    TimeProvider clock,
    ILogger<CheckCycle> logger)
{
    /// <summary>How many components are probed at once.</summary>
    public const int Parallelism = 8;

    public async Task<CycleResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var components = await db.Components
            .Where(c => c.Enabled)
            .OrderBy(c => c.Position)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (components.Count == 0)
        {
            NothingToCheck(logger);
            return new CycleResult(0, 0, 0);
        }

        // Probing is I/O and runs concurrently; persisting uses one DbContext and runs
        // afterwards, on one thread. Interleaving the two is the shortest route to a
        // second-operation-on-this-context crash under load.
        var observations = await ProbeAllAsync(components, cancellationToken).ConfigureAwait(false);

        var transitions = 0;
        var opened = 0;

        foreach (var (component, outcome) in observations)
        {
            var applied = await ApplyAsync(component, outcome, cancellationToken).ConfigureAwait(false);

            if (applied.Changed)
            {
                transitions++;
            }

            if (applied.IncidentOpened)
            {
                opened++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CycleFinished(logger, components.Count, transitions, opened);
        return new CycleResult(components.Count, transitions, opened);
    }

    private async Task<List<(Component Component, CheckOutcome Outcome)>> ProbeAllAsync(
        List<Component> components,
        CancellationToken cancellationToken)
    {
        var results = new (Component, CheckOutcome)[components.Count];
        using var gate = new SemaphoreSlim(Parallelism);

        var probes = components.Select(async (component, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outcome = await probe.ProbeAsync(component, cancellationToken).ConfigureAwait(false);
                results[index] = (component, outcome);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);
        return [.. results];
    }

    private async Task<(bool Changed, bool IncidentOpened)> ApplyAsync(
        Component component,
        CheckOutcome outcome,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var observed = component.CheckPolicy().Observe(outcome);

        var state = await db.CheckerState
            .SingleOrDefaultAsync(s => s.ComponentId == component.Id, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new CheckerState { ComponentId = component.Id };
            db.CheckerState.Add(state);
        }

        var open = await db.Intervals
            .Where(i => i.ComponentId == component.Id && i.EndedAt == null)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var committed = open?.State ?? ComponentState.Unknown;
        var before = new HysteresisState(committed, state.Candidate, state.ConsecutiveObservations);
        var after = component.Hysteresis().Advance(before, observed);

        state.Candidate = after.Candidate;
        state.ConsecutiveObservations = after.ConsecutiveObservations;
        state.LastCheckedAt = now;
        state.LastLatencyMs = (int)Math.Min(outcome.Latency.TotalMilliseconds, int.MaxValue);

        if (after.Committed == committed)
        {
            return (false, false);
        }

        // The old interval ends exactly where the new one begins. Ranges are half-open, so
        // the shared instant belongs to the new one and nothing is counted twice.
        if (open is not null)
        {
            open.EndedAt = now;
        }

        db.Intervals.Add(new ComponentInterval
        {
            ComponentId = component.Id,
            State = after.Committed,
            StartedAt = now,
            EndedAt = null,
        });

        StateChanged(logger, component.Slug, committed.ToString(), after.Committed.ToString());

        var incidentOpened = await MaybeOpenIncidentAsync(component, after.Committed, now, cancellationToken)
            .ConfigureAwait(false);

        return (true, incidentOpened);
    }

    private async Task<bool> MaybeOpenIncidentAsync(
        Component component,
        ComponentState committed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (committed != ComponentState.Down)
        {
            // Coming back up does not resolve anything. A person decides when an incident is
            // over, because "it answers again" and "it is fixed" are different claims and
            // only one of them is worth telling a reader.
            return false;
        }

        var alreadyOpen = await db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved)
            .AnyAsync(i => i.AffectedComponents.Any(c => c.Id == component.Id), cancellationToken)
            .ConfigureAwait(false);

        if (alreadyOpen)
        {
            return false;
        }

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            Title = component.Name + " is not responding",
            Status = IncidentStatus.Investigating,
            Impact = IncidentImpact.Major,
            StartedAt = now,
            OpenedAutomatically = true,
        };

        incident.AffectedComponents.Add(component);
        incident.Updates.Add(new IncidentUpdate
        {
            Body = "Automated checks stopped getting a healthy response from " + component.Name + ".",
            Status = IncidentStatus.Investigating,
            PostedAt = now,
        });

        db.Incidents.Add(incident);
        IncidentOpened(logger, component.Slug);
        return true;
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug, Message = "No enabled components to check")]
    private static partial void NothingToCheck(ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Checked {Probed} components: {Transitions} transitions, {Opened} incidents opened")]
    private static partial void CycleFinished(ILogger logger, int probed, int transitions, int opened);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "{Slug} moved from {From} to {To}")]
    private static partial void StateChanged(ILogger logger, string slug, string from, string to);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Opened an incident for {Slug}")]
    private static partial void IncidentOpened(ILogger logger, string slug);
}
