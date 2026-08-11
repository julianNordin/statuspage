using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StatusPage.Api.Contracts;
using StatusPage.Infrastructure.Identity;

namespace StatusPage.Api.Controllers;

/// <summary>
/// Signing in. There is no counterpart that signs up: operator accounts are seeded from
/// configuration, so adding one is a deployment rather than a form.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    SignInManager<OperatorAccount> signIn,
    UserManager<OperatorAccount> users,
    TokenIssuer tokens) : ControllerBase
{
    /// <summary>Exchanges credentials for a short-lived access token.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccessTokenResponse>> Token(
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var account = await users.FindByEmailAsync(request.Email).ConfigureAwait(false);

        // One answer for "no such account" and for "wrong password". Distinguishing them
        // turns this endpoint into a way of asking which addresses have accounts, and the
        // set of people who can declare an outage is not a list worth publishing.
        if (account is null)
        {
            return Unauthorized401();
        }

        var result = await signIn
            .CheckPasswordSignInAsync(account, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Unauthorized401();
        }

        var (token, expiresAt) = tokens.Issue(account);

        return new AccessTokenResponse(token, expiresAt, account.DisplayName);
    }

    private ObjectResult Unauthorized401() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Those credentials were not accepted.");
}
