using System.Net;
using StatusPage.Api.Contracts;

namespace StatusPage.Api.Tests;

/// <summary>
/// The SSRF rules, at the boundary an operator actually touches. The domain tests prove the
/// rules; these prove the endpoint asks them.
/// </summary>
[Collection(ApiUnderTest.Name)]
public class TargetUrlEndpointTests(ApiFactory factory)
{
    private static CreateComponentRequest WithTarget(string target) => new()
    {
        Name = "Anything",
        Slug = $"t-{Guid.NewGuid():N}"[..20],
        TargetUrl = target,
    };

    [Theory]
    [InlineData("http://169.254.169.254/metadata/identity/oauth2/token")]
    [InlineData("http://127.0.0.1:1433/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com/")]
    public async Task A_target_the_checker_must_never_fetch_is_refused_on_create(string target)
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/components", WithTarget(target), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_forbidden_target_cannot_be_smuggled_in_through_an_update_either()
    {
        // The create path and the update path are two doors into the same column. A guard on
        // one of them is not a guard.
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var created = await client.PostAsJsonAsync(
            "/api/components",
            WithTarget("https://example.com/health"),
            TestContext.Current.CancellationToken);
        var component = await created.Content.ReadJsonAsync<ComponentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(component);

        var updated = await client.PutAsJsonAsync(
            $"/api/components/{component.Id}",
            new UpdateComponentRequest
            {
                Name = component.Name,
                TargetUrl = "http://169.254.169.254/metadata/identity/oauth2/token",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_public_target_is_still_accepted()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/components",
            WithTarget("https://example.com/health"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
