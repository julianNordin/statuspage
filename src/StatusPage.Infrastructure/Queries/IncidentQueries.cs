using Microsoft.EntityFrameworkCore;
using StatusPage.Domain;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Queries;

/// <summary>Reads and writes over incidents and maintenance windows.</summary>
public sealed class IncidentQueries(StatusPageDbContext db)
{
    public async Task<IReadOnlyList<Incident>> RecentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await db.Incidents
            .AsNoTracking()
            .Include(i => i.AffectedComponents)
            .Include(i => i.Updates.OrderBy(u => u.PostedAt))
            .OrderByDescending(i => i.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Incident?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Incidents
            .Include(i => i.AffectedComponents)
            .Include(i => i.Updates.OrderBy(u => u.PostedAt))
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The open incident already covering a component, if there is one. The checker asks this
    /// before opening another: a component that is still down does not need a second incident
    /// every time it is checked.
    /// </summary>
    public async Task<Incident?> FindOpenForComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken = default) =>
        await db.Incidents
            .Include(i => i.AffectedComponents)
            .Where(i => i.Status != IncidentStatus.Resolved)
            .Where(i => i.AffectedComponents.Any(c => c.Id == componentId))
            .OrderByDescending(i => i.StartedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Component>> ComponentsBySlugAsync(
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken = default) =>
        await db.Components
            .Where(c => slugs.Contains(c.Slug))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaintenanceWindow>> MaintenanceAsync(
        DateTimeOffset from,
        CancellationToken cancellationToken = default) =>
        await db.MaintenanceWindows
            .AsNoTracking()
            .Include(m => m.AffectedComponents)
            .Where(m => m.EndsAt >= from)
            .OrderBy(m => m.StartsAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(MaintenanceWindow window, CancellationToken cancellationToken = default)
    {
        db.MaintenanceWindows.Add(window);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
