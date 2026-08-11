using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace StatusPage.Infrastructure.Identity;

/// <summary>One operator account, as described in configuration.</summary>
/// <param name="Email">Sign-in address.</param>
/// <param name="DisplayName">Shown beside incident updates.</param>
/// <param name="Password">Set only on creation; an existing account is never re-passworded here.</param>
public sealed record SeededOperator(string Email, string DisplayName, string Password);

/// <summary>
/// Creates the operator accounts a deployment says should exist.
/// <para>
/// This is the only way an account comes into being — there is no registration endpoint. An
/// open sign-up form on a status page would let a stranger tell your users you are down.
/// </para>
/// </summary>
public sealed partial class OperatorSeeder(
    UserManager<OperatorAccount> users,
    ILogger<OperatorSeeder> logger)
{
    public async Task SeedAsync(
        IReadOnlyCollection<SeededOperator> operators,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operators);

        foreach (var seed in operators)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await users.FindByEmailAsync(seed.Email).ConfigureAwait(false) is not null)
            {
                continue;
            }

            var account = new OperatorAccount
            {
                Id = Guid.CreateVersion7(),
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                DisplayName = seed.DisplayName,
            };

            var result = await users.CreateAsync(account, seed.Password).ConfigureAwait(false);

            if (result.Succeeded)
            {
                Created(logger, seed.Email);
                continue;
            }

            // Loud, not logged-and-carried-on. A deployment that silently ends up with no
            // operators is one nobody can administer, and the symptom — every sign-in
            // returning 401 — points at the credentials rather than at the seeding that
            // never happened. The error codes name the rule that was broken, never the
            // password itself.
            var codes = string.Join("; ", result.Errors.Select(e => e.Code));
            Refused(logger, seed.Email, codes);

            throw new InvalidOperationException(
                $"Could not create the configured operator '{seed.Email}': {codes}");
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Seeded operator {Email}")]
    private static partial void Created(ILogger logger, string email);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Could not seed operator {Email}: {Errors}")]
    private static partial void Refused(ILogger logger, string email, string errors);
}
