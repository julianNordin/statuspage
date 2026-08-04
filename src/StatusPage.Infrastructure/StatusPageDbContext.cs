using Microsoft.EntityFrameworkCore;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure;

/// <summary>
/// The only place in the solution that talks to the database. Everything above it asks a
/// query or a command object; nothing above it holds a <see cref="DbContext"/>.
/// </summary>
public class StatusPageDbContext(DbContextOptions<StatusPageDbContext> options) : DbContext(options)
{
    public DbSet<Component> Components => Set<Component>();

    public DbSet<ComponentInterval> Intervals => Set<ComponentInterval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StatusPageDbContext).Assembly);
    }
}
