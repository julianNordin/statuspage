using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure.Configuration;
using StatusPage.Infrastructure.Identity;

namespace StatusPage.Infrastructure;

/// <summary>
/// The only place in the solution that talks to the database. Everything above it asks a
/// query or a command object; nothing above it holds a <see cref="DbContext"/>.
/// </summary>
public class StatusPageDbContext(DbContextOptions<StatusPageDbContext> options)
    : IdentityDbContext<OperatorAccount, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Component> Components => Set<Component>();

    public DbSet<ComponentInterval> Intervals => Set<ComponentInterval>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentUpdate> IncidentUpdates => Set<IncidentUpdate>();

    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Identity's own configuration first — it maps eight tables of its own and this
        // overload is where they are declared.
        base.OnModelCreating(builder);

        builder.ConfigureIdentity();
        builder.ApplyConfigurationsFromAssembly(typeof(StatusPageDbContext).Assembly);
    }
}
