using Microsoft.EntityFrameworkCore;
using StatusPage.Domain;

namespace StatusPage.Infrastructure.ReadModel;

/// <summary>
/// Projects the database into the documents the checker and the page read.
/// <para>
/// This is the write model becoming a read model. SQL is the authoritative log — intervals,
/// incidents, maintenance — and these files are derived from it. Rebuilding them from the log
/// is therefore always possible, which is what makes it safe for the serving path never to
/// touch the log at all.
/// </para>
/// </summary>
public sealed class ReadModelProjection(StatusPageDbContext db, IReadModelStore store)
{
    /// <summary>Days of history the snapshot carries.</summary>
    public const int WindowDays = 90;

    /// <summary>Writes config.json from the current component catalogue.</summary>
    public async Task WriteConfigAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var components = await db.Components
            .AsNoTracking()
            .Where(c => c.Enabled)
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new CheckerComponent(
                c.Id, c.Slug, c.Name, c.TargetUrl, c.ExpectedStatusCode, c.DegradedAboveMs,
                c.FailuresToOpen, c.SuccessesToClose, c.Position))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await store.WriteAsync(
            ReadModelDocuments.Config,
            new CheckerConfig(now, components),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds status.json from the log. Run after a transition, and available to an operator
    /// as a repair when the read model and the database disagree.
    /// </summary>
    public async Task<StatusSnapshot> WriteSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var window = new TimeRange(now.AddDays(-WindowDays), now);

        var components = await db.Components
            .AsNoTracking()
            .Where(c => c.Enabled)
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = components.ConvertAll(c => c.Id);

        var intervals = await db.Intervals
            .AsNoTracking()
            .Where(i => ids.Contains(i.ComponentId))
            // An open interval is always relevant, whatever the window says. One that started
            // at this very instant contributes no duration to the uptime sum — correctly — but
            // it is still what the component is doing now, and a strict "started before the
            // window ends" drops it and reports Unknown.
            .Where(i => i.EndedAt == null || (i.StartedAt < window.End && i.EndedAt > window.Start))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var maintenance = await db.MaintenanceWindows
            .AsNoTracking()
            .Include(m => m.AffectedComponents)
            .Where(m => m.EndsAt > window.Start)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var incidents = await db.Incidents
            .AsNoTracking()
            .Include(i => i.AffectedComponents)
            .Include(i => i.Updates)
            .OrderByDescending(i => i.StartedAt)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byComponent = intervals.GroupBy(i => i.ComponentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var snapshotComponents = new List<SnapshotComponent>(components.Count);

        foreach (var component in components)
        {
            var own = byComponent.TryGetValue(component.Id, out var rows) ? rows : [];
            var domain = own.ConvertAll(i => i.ToDomain());

            var excluded = maintenance
                .Where(m => m.AffectedComponents.Any(c => c.Id == component.Id))
                .Select(m => m.ToRange())
                .ToList();

            var report = Uptime.Measure(domain, excluded, window, now);
            var current = own.Find(i => i.EndedAt is null);

            snapshotComponents.Add(new SnapshotComponent(
                component.Slug,
                component.Name,
                current?.State ?? ComponentState.Unknown,
                current?.StartedAt,
                null,
                report.Ratio,
                report.Measured.TotalHours,
                DailyBars(domain, excluded, now)));
        }

        var snapshot = new StatusSnapshot(
            now,
            Worst(snapshotComponents),
            snapshotComponents,
            [.. incidents.Select(i => new SnapshotIncident(
                i.Id, i.Title, i.Status, i.Impact, i.StartedAt, i.ResolvedAt,
                [.. i.AffectedComponents.Select(c => c.Slug).Order(StringComparer.Ordinal)],
                [.. i.Updates.OrderBy(u => u.PostedAt)
                    .Select(u => new SnapshotUpdate(u.Body, u.Status, u.PostedAt, u.PostedByDisplayName))]))],
            [.. maintenance.Where(m => m.EndsAt >= now).OrderBy(m => m.StartsAt)
                .Select(m => new SnapshotMaintenance(
                    m.Title, m.Description, m.StartsAt, m.EndsAt,
                    [.. m.AffectedComponents.Select(c => c.Slug).Order(StringComparer.Ordinal)]))]);

        await store.WriteAsync(ReadModelDocuments.Status, snapshot, cancellationToken)
            .ConfigureAwait(false);

        return snapshot;
    }

    /// <summary>
    /// One bar per day. Computed from the intervals rather than stored, because the intervals
    /// are already the compressed form — ninety numbers out of a handful of rows.
    /// </summary>
    private static List<SnapshotDay> DailyBars(
        List<StateInterval> intervals,
        List<TimeRange> maintenance,
        DateTimeOffset now)
    {
        var days = new List<SnapshotDay>(WindowDays);
        var midnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        for (var offset = WindowDays - 1; offset >= 0; offset--)
        {
            var start = midnight.AddDays(-offset);
            var day = new TimeRange(start, start.AddDays(1));
            var report = Uptime.Measure(intervals, maintenance, day, now);

            var worst = ComponentState.Unknown;
            foreach (var interval in intervals)
            {
                if (!interval.ClipTo(day, now).IsEmpty && Severity(interval.State) > Severity(worst))
                {
                    worst = interval.State;
                }
            }

            days.Add(new SnapshotDay(DateOnly.FromDateTime(start.UtcDateTime), report.Ratio, worst));
        }

        return days;
    }

    private static ComponentState Worst(IEnumerable<SnapshotComponent> components)
    {
        var worst = ComponentState.Unknown;
        foreach (var component in components)
        {
            if (Severity(component.State) > Severity(worst))
            {
                worst = component.State;
            }
        }

        return worst;
    }

    private static int Severity(ComponentState state) => state switch
    {
        ComponentState.Up => 1,
        ComponentState.Degraded => 2,
        ComponentState.Down => 3,
        _ => 0,
    };
}
