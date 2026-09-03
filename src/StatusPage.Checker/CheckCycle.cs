using Microsoft.EntityFrameworkCore;
using StatusPage.Checker.Probing;
using StatusPage.Domain;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.ReadModel;

namespace StatusPage.Checker;

/// <summary>What one pass over every component came to.</summary>
/// <param name="Probed">How many components were checked.</param>
/// <param name="Transitions">How many changed state, and so wrote to the database.</param>
/// <param name="IncidentsOpened">How many outages the checker declared by itself.</param>
/// <param name="TouchedDatabase">
/// Whether this cycle opened a database connection at all. False on a quiet cycle, which is
/// almost all of them, and the whole reason the read model exists.
/// </param>
public sealed record CycleResult(int Probed, int Transitions, int IncidentsOpened, bool TouchedDatabase);

/// <summary>
/// One pass: read the configuration from a file, probe every component, and write to the
/// database only if something actually changed.
/// <para>
/// The file is not a cache. Azure SQL's free offer meters <em>awake</em> time and auto-pause
/// needs sixty unbroken idle minutes, so a checker that read its configuration from the
/// database every ten minutes would keep it awake permanently — roughly 1.3 million vCore
/// seconds a month against an allowance of 100,000. Reading configuration from blob storage
/// makes the database's only visitors an operator and a genuine state change, and both are
/// rare enough that it sleeps.
/// </para>
/// </summary>
public sealed partial class CheckCycle(
    StatusPageDbContext db,
    IReadModelStore store,
    ReadModelProjection projection,
    ITargetProbe probe,
    TimeProvider clock,
    ILogger<CheckCycle> logger)
{
    /// <summary>How many components are probed at once.</summary>
    public const int Parallelism = 8;

    public async Task<CycleResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var config = await store.ReadAsync<CheckerConfig>(ReadModelDocuments.Config, cancellationToken)
            .ConfigureAwait(false);

        if (config is null || config.Components.Count == 0)
        {
            // No configuration written yet. Not an error: it is the state a deployment starts
            // in, before an operator has added anything. Said at Information rather than Debug
            // because this is the only line a cycle on that path produces, and at Debug it
            // produced none at all — fifteen scheduled runs in the first deployment reported
            // success and wrote nothing anywhere, which is indistinguishable from a checker
            // that crashed on start-up. Whether it found any configuration is exactly the
            // thing worth knowing.
            NothingToCheck(logger, config is null ? "absent" : "empty");
            return new CycleResult(0, 0, 0, false);
        }

        var memory = await store.ReadAsync<CheckerMemory>(ReadModelDocuments.CheckerState, cancellationToken)
            .ConfigureAwait(false) ?? CheckerMemory.Empty;

        var observations = await ProbeAllAsync(config.Components, cancellationToken).ConfigureAwait(false);

        var next = new Dictionary<string, ComponentMemory>(StringComparer.Ordinal);
        var transitions = new List<(CheckerComponent Component, ComponentState To)>();

        foreach (var (component, outcome) in observations)
        {
            var before = memory.Components.TryGetValue(component.Slug, out var remembered)
                ? remembered
                : new ComponentMemory(ComponentState.Unknown, ComponentState.Unknown, 0, null, null, null);

            var observed = component.CheckPolicy().Observe(outcome);
            var after = component.Hysteresis().Advance(before.ToHysteresisState(), observed);
            var changed = after.Committed != before.Committed;

            if (changed)
            {
                transitions.Add((component, after.Committed));
                StateChanged(logger, component.Slug, before.Committed.ToString(), after.Committed.ToString());
            }

            next[component.Slug] = new ComponentMemory(
                after.Committed,
                after.Candidate,
                after.ConsecutiveObservations,
                changed ? now : before.Since,
                now,
                (int)Math.Min(outcome.Latency.TotalMilliseconds, int.MaxValue));
        }

        await store.WriteAsync(
            ReadModelDocuments.CheckerState,
            new CheckerMemory(now, next),
            cancellationToken).ConfigureAwait(false);

        var opened = 0;

        if (transitions.Count > 0)
        {
            // Something actually changed, so the log has to hear about it. This is the only
            // path that opens a database connection.
            opened = await RecordAsync(transitions, now, cancellationToken).ConfigureAwait(false);
            await projection.WriteSnapshotAsync(now, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Nothing changed. Refresh the snapshot from what is already in it, so the page
            // still shows a recent timestamp and current latencies without anybody reading
            // the database.
            await RefreshSnapshotAsync(now, next, cancellationToken).ConfigureAwait(false);
        }

        CycleFinished(logger, config.Components.Count, transitions.Count, opened, transitions.Count > 0);
        return new CycleResult(config.Components.Count, transitions.Count, opened, transitions.Count > 0);
    }

    private async Task<List<(CheckerComponent Component, CheckOutcome Outcome)>> ProbeAllAsync(
        IReadOnlyList<CheckerComponent> components,
        CancellationToken cancellationToken)
    {
        var results = new (CheckerComponent, CheckOutcome)[components.Count];
        using var gate = new SemaphoreSlim(Parallelism);

        var probes = components.Select(async (component, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outcome = await probe.ProbeAsync(component.TargetUrl, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Writes the transitions to the log, and opens incidents where they are owed.</summary>
    private async Task<int> RecordAsync(
        List<(CheckerComponent Component, ComponentState To)> transitions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var opened = 0;

        foreach (var (component, to) in transitions)
        {
            var open = await db.Intervals
                .Where(i => i.ComponentId == component.Id && i.EndedAt == null)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            // The old interval ends exactly where the new one begins. Ranges are half-open,
            // so the shared instant belongs to the new one and nothing is counted twice.
            if (open is not null)
            {
                open.EndedAt = now;
            }

            db.Intervals.Add(new ComponentInterval
            {
                ComponentId = component.Id,
                State = to,
                StartedAt = now,
                EndedAt = null,
            });

            if (to == ComponentState.Down &&
                await OpenIncidentAsync(component, now, cancellationToken).ConfigureAwait(false))
            {
                opened++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return opened;
    }

    private async Task<bool> OpenIncidentAsync(
        CheckerComponent component,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alreadyOpen = await db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved)
            .AnyAsync(i => i.AffectedComponents.Any(c => c.Id == component.Id), cancellationToken)
            .ConfigureAwait(false);

        if (alreadyOpen)
        {
            return false;
        }

        var entity = await db.Components
            .SingleAsync(c => c.Id == component.Id, cancellationToken)
            .ConfigureAwait(false);

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            Title = component.Name + " is not responding",
            Status = IncidentStatus.Investigating,
            Impact = IncidentImpact.Major,
            StartedAt = now,
            OpenedAutomatically = true,
        };

        incident.AffectedComponents.Add(entity);
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

    /// <summary>
    /// Updates the snapshot in place from the checker's own memory. No database: state has not
    /// changed, so the interval history behind the uptime bars has not changed either, and the
    /// only things worth refreshing are the timestamp and the latencies.
    /// </summary>
    private async Task RefreshSnapshotAsync(
        DateTimeOffset now,
        Dictionary<string, ComponentMemory> memory,
        CancellationToken cancellationToken)
    {
        var previous = await store.ReadAsync<StatusSnapshot>(ReadModelDocuments.Status, cancellationToken)
            .ConfigureAwait(false);

        if (previous is null)
        {
            // Nothing to patch. The next transition rebuilds it from the log, and until then
            // the page correctly shows that nothing is known.
            return;
        }

        var components = previous.Components
            .Select(c => memory.TryGetValue(c.Slug, out var m)
                ? c with { State = m.Committed, Since = m.Since ?? c.Since, LastLatencyMs = m.LastLatencyMs }
                : c)
            .ToList();

        await store.WriteAsync(
            ReadModelDocuments.Status,
            previous with { GeneratedAt = now, Components = components },
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Nothing to check: the configuration document is {State}")]
    private static partial void NothingToCheck(ILogger logger, string state);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Checked {Probed}: {Transitions} transitions, {Opened} incidents, database touched: {Touched}")]
    private static partial void CycleFinished(
        ILogger logger, int probed, int transitions, int opened, bool touched);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "{Slug} moved from {From} to {To}")]
    private static partial void StateChanged(ILogger logger, string slug, string from, string to);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Opened an incident for {Slug}")]
    private static partial void IncidentOpened(ILogger logger, string slug);
}
