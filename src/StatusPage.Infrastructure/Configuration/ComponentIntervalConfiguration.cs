using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Configuration;

internal sealed class ComponentIntervalConfiguration : IEntityTypeConfiguration<ComponentInterval>
{
    public void Configure(EntityTypeBuilder<ComponentInterval> builder)
    {
        // The overlap rule is a trigger (SQL Server has no exclusion constraint), and EF has
        // to be told so. Its default INSERT uses an OUTPUT clause to read identity values
        // back, which SQL Server forbids on a table with enabled triggers; declaring the
        // trigger makes EF fall back to a slower technique that works. Without this line
        // every write to this table fails, including the ones the trigger would have allowed.
        builder.ToTable("component_intervals", t => t.HasTrigger("tr_component_intervals_no_overlap"));

        builder.HasKey(i => i.Id);

        builder.Property(i => i.State).HasConversion<int>();

        // Every read is "this component, newest first" or "this component, inside a window".
        builder.HasIndex(i => new { i.ComponentId, i.StartedAt })
            .HasDatabaseName("ix_component_intervals_component_started");
    }
}
