using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StatusPage.Api.Contracts;

namespace StatusPage.Api.Tests;

[Collection(ApiUnderTest.Name)]
public class AuthenticationTests(ApiFactory factory)
{
    private static readonly object GoodCredentials = new
    {
        Email = ApiFactory.OperatorEmail,
        Password = ApiFactory.OperatorPassword,
    };

    [Fact]
    public async Task An_operator_who_knows_the_password_gets_a_token()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/token", GoodCredentials, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.NotEmpty(body.AccessToken);
        Assert.Equal("Sam Operator", body.DisplayName);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/token",
            new { Email = ApiFactory.OperatorEmail, Password = "Not-The-Password-1" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_address_with_no_account_is_refused_identically_to_a_wrong_password()
    {
        // Two different answers here would turn this endpoint into a way of asking which
        // addresses have operator accounts, and the set of people who can declare an outage
        // is not a list worth publishing. The bodies are compared, not just the statuses.
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/token",
            new { Email = ApiFactory.OperatorEmail, Password = "Not-The-Password-1" },
            TestContext.Current.CancellationToken);

        var noSuchAccount = await client.PostAsJsonAsync(
            "/api/auth/token",
            new { Email = "nobody@example.test", Password = "Not-The-Password-1" },
            TestContext.Current.CancellationToken);

        Assert.Equal(wrongPassword.StatusCode, noSuchAccount.StatusCode);

        var first = await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var second = await noSuchAccount.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // traceId differs per request; everything else must match.
        Assert.Equal(WithoutTraceId(first), WithoutTraceId(second));
    }

    private static string WithoutTraceId(string body)
    {
        var index = body.IndexOf("\"traceId\"", StringComparison.Ordinal);
        return index < 0 ? body : body[..index];
    }

    [Theory]
    [InlineData("GET", "/api/components")]
    [InlineData("POST", "/api/components")]
    [InlineData("GET", "/api/components/anything")]
    public async Task The_component_endpoints_refuse_a_caller_with_no_token(string method, string path)
    {
        // ComponentsController carries no [Authorize] attribute at all. It is protected by the
        // fallback policy, which is the point: a new controller added later is closed by
        // default, and forgetting the attribute is a 401 in this test rather than an open
        // write endpoint in production. Removing the FallbackPolicy makes these fail.
        var client = factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_string_that_is_not_a_token_at_all_is_refused()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        var response = await client.GetAsync("/api/components", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_refused()
    {
        // The malformed-string test above passes whether or not signatures are checked at
        // all — it never reaches validation, because it does not parse. Turning off
        // ValidateIssuerSigningKey broke no test until this one existed. This token is
        // well-formed, unexpired, and carries the right issuer, audience and claims; the
        // only thing wrong with it is who signed it.
        var forged = new JwtSecurityToken(
            issuer: "statuspage-tests",
            audience: "statuspage-tests",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    "a-different-key-entirely-0123456789abcdefghij")),
                SecurityAlgorithms.HmacSha256));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(forged));

        var response = await client.GetAsync("/api/components", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/status")]
    [InlineData("/health")]
    public async Task The_public_surfaces_answer_without_a_token(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_way_to_create_an_account()
    {
        // Operator accounts are seeded from configuration. If somebody adds a registration
        // endpoint later, this is the test that objects.
        var client = factory.CreateClient();

        foreach (var path in new[] { "/api/auth/register", "/api/auth/signup", "/api/operators" })
        {
            var response = await client.PostAsJsonAsync(
                path,
                new { Email = "stranger@example.test", Password = "Stranger-Password-1" },
                TestContext.Current.CancellationToken);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized
                    or HttpStatusCode.MethodNotAllowed,
                $"{path} answered {(int)response.StatusCode}; registration must not exist.");
        }
    }
}
