using System.ComponentModel.DataAnnotations;

namespace StatusPage.Api.Contracts;

/// <summary>Credentials for an operator who already has an account.</summary>
public sealed record SignInRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

/// <summary>An access token and when it stops working.</summary>
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string DisplayName);
