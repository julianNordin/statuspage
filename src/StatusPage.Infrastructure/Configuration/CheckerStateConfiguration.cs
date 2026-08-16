using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Configuration;

internal sealed class CheckerStateConfiguration : IEntityTypeConfiguration<CheckerState>
{
    public void Configure(EntityTypeBuilder<CheckerState> builder)
    {
        builder.ToTable("checker_state");

        // One row per component, and the component's own id is the key. There is nothing to
        // say about a component twice.
        builder.HasKey(s => s.ComponentId);

        builder.Property(s => s.Candidate).HasConversion<int>();

        builder.HasOne(s => s.Component)
            .WithOne()
            .HasForeignKey<CheckerState>(s => s.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
