using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StatusPage.Infrastructure.Identity;

namespace StatusPage.Infrastructure.Configuration;

/// <summary>
/// Identity's tables, renamed to match the rest of the schema. Its defaults are PascalCase
/// and prefixed <c>AspNet</c>; everything else here is snake_case. One schema, one convention.
/// </summary>
internal static class IdentityConfiguration
{
    public static void ConfigureIdentity(this ModelBuilder builder)
    {
        builder.Entity<OperatorAccount>(b =>
        {
            b.ToTable("operators");
            b.Property(o => o.DisplayName).HasMaxLength(80).IsRequired();
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("operator_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("operator_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("operator_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("operator_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        // IdentityUserPasskey is deliberately not renamed. Registering it explicitly makes EF
        // rediscover its owned IdentityPasskeyData without the base configuration's key, and
        // the model then fails to build: "The entity type 'IdentityPasskeyData' requires a
        // primary key". It keeps its default name; passkeys are unused here anyway.
    }
}
