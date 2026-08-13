using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Configuration;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Title).HasMaxLength(160).IsRequired();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.Impact).HasConversion<int>();

        // The public page reads "recent incidents, newest first" and nothing else.
        builder.HasIndex(i => i.StartedAt).HasDatabaseName("ix_incidents_started");

        builder.HasMany(i => i.Updates)
            .WithOne(u => u.Incident)
            .HasForeignKey(u => u.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.AffectedComponents)
            .WithMany(c => c.Incidents)
            .UsingEntity(join => join.ToTable("incident_components"));
    }
}

internal sealed class IncidentUpdateConfiguration : IEntityTypeConfiguration<IncidentUpdate>
{
    public void Configure(EntityTypeBuilder<IncidentUpdate> builder)
    {
        builder.ToTable("incident_updates");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Body).HasMaxLength(4000).IsRequired();
        builder.Property(u => u.Status).HasConversion<int>();
        builder.Property(u => u.PostedByDisplayName).HasMaxLength(80);

        builder.HasIndex(u => new { u.IncidentId, u.PostedAt })
            .HasDatabaseName("ix_incident_updates_incident_posted");
    }
}

internal sealed class MaintenanceWindowConfiguration : IEntityTypeConfiguration<MaintenanceWindow>
{
    public void Configure(EntityTypeBuilder<MaintenanceWindow> builder)
    {
        builder.ToTable("maintenance_windows");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(160).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(4000);

        builder.HasIndex(m => new { m.StartsAt, m.EndsAt })
            .HasDatabaseName("ix_maintenance_windows_span");

        builder.HasMany(m => m.AffectedComponents)
            .WithMany(c => c.MaintenanceWindows)
            .UsingEntity(join => join.ToTable("maintenance_components"));
    }
}
