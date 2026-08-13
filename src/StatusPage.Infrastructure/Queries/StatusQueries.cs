using Microsoft.EntityFrameworkCore;
using StatusPage.Domain;

namespace StatusPage.Infrastructure.Queries;

/// <summary>One component's current state and how available it has been.</summary>
/// <param name="Slug">Stable identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="State">Current committed state.</param>
/// <param name="Since">When it entered that state.</param>
/// <param name="Uptime">Availability over the window, or null when nothing was accountable.</param>
/// <param name="Measured">How much of the window was observed.</param>
public sealed record ComponentStatus(
    string Slug,
    string Name,
    ComponentState State,
    DateTimeOffset? Since,
    double? Uptime,
    TimeSpan Measured);

/// <summary>
/// Reads that answer "what is happening, and how has it been going". These are the queries
/// the snapshot is built from, so they are also the queries the read model replaces on the
/// public path — see Phase 10.
/// </summary>
public sealed class StatusQueries(StatusPageDbContext db)
{
    /// <summary>
    /// Every enabled component with its current state and its availability over
    /// <paramref name="window"/>.
    /// </summary>
    public async Task<IReadOnlyList<ComponentStatus>> ReadAsync(
        TimeRange window,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        // One query for the components and one for the intervals that touch the window,
        // rather than one query per component. An interval is relevant if it starts before
        // the window ends and has not finished before the window starts.
        var components = await db.Components
            .Where(c => c.Enabled)
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = components.ConvertAll(c => c.Id);

        var intervals = await db.Intervals
            .Where(i => ids.Contains(i.ComponentId))
            .Where(i => i.StartedAt < window.End && (i.EndedAt == null || i.EndedAt > window.Start))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byComponent = intervals
            .GroupBy(i => i.ComponentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Announced maintenance comes out of the denominator, so it has to be loaded with
        // the same window filter the intervals use. A window is relevant if it starts before
        // the reporting window ends and ends after it starts.
        var maintenance = await db.MaintenanceWindows
            .Where(m => m.StartsAt < window.End && m.EndsAt > window.Start)
            .Select(m => new
            {
                m.StartsAt,
                m.EndsAt,
                ComponentIds = m.AffectedComponents.Select(c => c.Id).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var maintenanceByComponent = new Dictionary<Guid, List<TimeRange>>();
        foreach (var entry in maintenance)
        {
            var range = new TimeRange(entry.StartsAt, entry.EndsAt);
            foreach (var componentId in entry.ComponentIds)
            {
                if (!maintenanceByComponent.TryGetValue(componentId, out var ranges))
                {
                    ranges = [];
                    maintenanceByComponent[componentId] = ranges;
                }

                ranges.Add(range);
            }
        }

        var results = new List<ComponentStatus>(components.Count);

        foreach (var component in components)
        {
            var own = byComponent.TryGetValue(component.Id, out var rows) ? rows : [];
            var current = own.Find(i => i.EndedAt is null);
            var excluded = maintenanceByComponent.TryGetValue(component.Id, out var windows)
                ? windows
                : [];

            var report = Uptime.Measure(own.Select(i => i.ToDomain()), excluded, window, asOf);

            results.Add(new ComponentStatus(
                component.Slug,
                component.Name,
                current?.State ?? ComponentState.Unknown,
                current?.StartedAt,
                report.Ratio,
                report.Measured));
        }

        return results;
    }

    /// <summary>
    /// The single state a whole page reports. The worst of its components, because a page
    /// claiming to be up while something on it is down is the one thing a status page must
    /// never do. A page with no components at all is Unknown rather than Up.
    /// </summary>
    public static ComponentState Overall(IEnumerable<ComponentStatus> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var worst = ComponentState.Unknown;
        var seen = false;

        foreach (var component in components)
        {
            seen = true;
            if (Severity(component.State) > Severity(worst))
            {
                worst = component.State;
            }
        }

        return seen ? worst : ComponentState.Unknown;
    }

    private static int Severity(ComponentState state) => state switch
    {
        ComponentState.Up => 1,
        ComponentState.Degraded => 2,
        ComponentState.Down => 3,
        _ => 0,
    };
}
