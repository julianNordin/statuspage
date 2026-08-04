using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Configuration;

internal sealed class ComponentConfiguration : IEntityTypeConfiguration<Component>
{
    public void Configure(EntityTypeBuilder<Component> builder)
    {
        builder.ToTable("components");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(80).IsRequired();

        // Long enough for a real URL and short enough to index. Anything longer is not a
        // health-check endpoint.
        builder.Property(c => c.TargetUrl).HasMaxLength(2048).IsRequired();

        // A slug is how the public snapshot names a component, so two components sharing one
        // would silently overwrite each other in the read model.
        builder.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("ux_components_slug");

        builder.HasMany(c => c.Intervals)
            .WithOne(i => i.Component)
            .HasForeignKey(i => i.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
