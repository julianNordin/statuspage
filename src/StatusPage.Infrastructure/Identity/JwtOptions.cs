using System.ComponentModel.DataAnnotations;

namespace StatusPage.Infrastructure.Identity;

/// <summary>
/// How access tokens are signed and who will accept them. Bound from configuration and
/// validated at startup, so a deployment missing the signing key fails to start rather than
/// issuing tokens nobody can verify.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The symmetric signing key. Comes from Key Vault in a deployed environment, through a
    /// managed identity — it is never an application setting with a value in it.
    /// </summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Deliberately short. There are no refresh tokens here: an operator console is used in
    /// sittings, and signing in again is a smaller cost than a rotation scheme nobody needs.
    /// </summary>
    [Range(1, 24 * 60)]
    public int LifetimeMinutes { get; set; } = 60;
}
