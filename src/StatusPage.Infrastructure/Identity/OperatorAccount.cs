using Microsoft.AspNetCore.Identity;

namespace StatusPage.Infrastructure.Identity;

/// <summary>
/// Somebody who can change what the status page says.
/// <para>
/// There is no public registration and no self-service anything. A status page has a small,
/// known set of people allowed to declare an outage, and an open sign-up form on it would be
/// a way for a stranger to tell your users you are down. Operators are seeded from
/// configuration; adding one is a deployment, which is the correct amount of friction.
/// </para>
/// <para>
/// This lives in infrastructure rather than in the domain because it exists only because
/// ASP.NET Core Identity exists. The domain project references no framework and this type
/// could not be written there.
/// </para>
/// </summary>
/// <remarks>
/// Named <c>OperatorAccount</c> rather than <c>Operator</c>: the shorter name is a reserved
/// word in some .NET languages and the analyzers refuse it.
/// </remarks>
public class OperatorAccount : IdentityUser<Guid>
{
    /// <summary>Shown beside incident updates, so a reader sees a person rather than an email.</summary>
    public required string DisplayName { get; set; }
}
