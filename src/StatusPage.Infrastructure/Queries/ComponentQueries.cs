using Microsoft.EntityFrameworkCore;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Queries;

/// <summary>Reads and writes over the component catalogue.</summary>
public sealed class ComponentQueries(StatusPageDbContext db)
{
    public async Task<IReadOnlyList<Component>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Components
            .AsNoTracking()
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Component?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await db.Components
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

    public async Task<Component?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Components
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        await db.Components
            .AnyAsync(c => c.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(Component component, CancellationToken cancellationToken = default)
    {
        db.Components.Add(component);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    public async Task RemoveAsync(Component component, CancellationToken cancellationToken = default)
    {
        db.Components.Remove(component);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
