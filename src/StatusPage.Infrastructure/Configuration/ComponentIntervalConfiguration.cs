using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Configuration;

internal sealed class ComponentIntervalConfiguration : IEntityTypeConfiguration<ComponentInterval>
{
    public void Configure(EntityTypeBuilder<ComponentInterval> builder)
    {
        builder.ToTable("component_intervals");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.State).HasConversion<int>();

        // Every read is "this component, newest first" or "this component, inside a window".
        builder.HasIndex(i => new { i.ComponentId, i.StartedAt })
            .HasDatabaseName("ix_component_intervals_component_started");
    }
}
