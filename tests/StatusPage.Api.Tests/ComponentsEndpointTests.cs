using System.Net;
using StatusPage.Api.Contracts;

namespace StatusPage.Api.Tests;

[Collection(ApiUnderTest.Name)]
public class ComponentsEndpointTests(ApiFactory factory)
{
    private static CreateComponentRequest NewRequest(string slug) => new()
    {
        Name = "The API",
        Slug = slug,
        TargetUrl = "https://example.com/health",
    };

    private static string UniqueSlug() => $"api-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task A_created_component_comes_back_from_the_location_it_reports()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var request = NewRequest(UniqueSlug());

        var created = await client.PostAsJsonAsync(
            "/api/components", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var fetched = await client.GetAsync(
            created.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var body = await fetched.Content.ReadJsonAsync<ComponentResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(request.Slug, body.Slug);
        Assert.Equal("The API", body.Name);
    }

    [Fact]
    public async Task A_slug_that_is_already_taken_is_a_conflict_and_says_so_as_problem_details()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var request = NewRequest(UniqueSlug());

        await client.PostAsJsonAsync("/api/components", request, TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync(
            "/api/components", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_component_that_does_not_exist_is_a_404()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync(
            "/api/components/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_slug_that_is_not_a_slug_is_refused_before_it_reaches_the_database()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/components",
            NewRequest("Not A Slug!"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_threshold_outside_the_allowed_range_is_refused(int failuresToOpen)
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var request = NewRequest(UniqueSlug()) with { FailuresToOpen = failuresToOpen };

        var response = await client.PostAsJsonAsync(
            "/api/components", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_component_can_be_updated_and_then_deleted()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var created = await client.PostAsJsonAsync(
            "/api/components", NewRequest(UniqueSlug()), TestContext.Current.CancellationToken);
        var component = await created.Content.ReadJsonAsync<ComponentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(component);

        var updated = await client.PutAsJsonAsync(
            $"/api/components/{component.Id}",
            new UpdateComponentRequest
            {
                Name = "Renamed",
                TargetUrl = component.TargetUrl,
                DegradedAboveMs = 900,
                Enabled = false,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await updated.Content.ReadJsonAsync<ComponentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal("Renamed", after.Name);
        Assert.Equal(900, after.DegradedAboveMs);
        Assert.False(after.Enabled);

        var deleted = await client.DeleteAsync(
            $"/api/components/{component.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await client.GetAsync(
            $"/api/components/{component.Slug}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Health_answers_without_saying_when_it_was_built()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body, StringComparison.Ordinal);

        // A build timestamp on a public endpoint is a date leak with no upside. If somebody
        // adds one, this fails.
        Assert.DoesNotContain("built", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20", body, StringComparison.Ordinal);
    }
}
