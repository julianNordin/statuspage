using System.Net;
using StatusPage.Api.Contracts;
using StatusPage.Domain;
using StatusPage.Infrastructure.ReadModel;

namespace StatusPage.Api.Tests;

[Collection(ApiUnderTest.Name)]
public class ReadModelEndpointTests(ApiFactory factory)
{
    private static CreateComponentRequest NewComponent(string slug) => new()
    {
        Name = "Published service",
        Slug = slug,
        TargetUrl = "https://example.com/health",
        DegradedAboveMs = 750,
    };

    [Fact]
    public async Task Adding_a_component_publishes_the_configuration_the_checker_reads()
    {
        // The checker reads its configuration from a file, so a change nobody publishes is a
        // change it never sees — the component would sit in the database being checked by
        // nothing at all.
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = $"p-{Guid.NewGuid():N}"[..20];

        var created = await client.PostAsJsonAsync(
            "/api/components", NewComponent(slug), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var config = await factory.ReadModel.ReadAsync<CheckerConfig>(
            ReadModelDocuments.Config, TestContext.Current.CancellationToken);

        Assert.NotNull(config);
        var published = config.Components.SingleOrDefault(c => c.Slug == slug);
        Assert.NotNull(published);
        Assert.Equal("https://example.com/health", published.TargetUrl);
        Assert.Equal(750, published.DegradedAboveMs);
    }

    [Fact]
    public async Task Disabling_a_component_takes_it_out_of_the_configuration()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = $"p-{Guid.NewGuid():N}"[..20];

        var created = await client.PostAsJsonAsync(
            "/api/components", NewComponent(slug), TestContext.Current.CancellationToken);
        var component = await created.Content.ReadJsonAsync<ComponentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(component);

        await client.PutAsJsonAsync(
            $"/api/components/{component.Id}",
            new UpdateComponentRequest
            {
                Name = component.Name,
                TargetUrl = component.TargetUrl,
                Enabled = false,
            },
            TestContext.Current.CancellationToken);

        var config = await factory.ReadModel.ReadAsync<CheckerConfig>(
            ReadModelDocuments.Config, TestContext.Current.CancellationToken);

        Assert.NotNull(config);
        Assert.DoesNotContain(config.Components, c => c.Slug == slug);
    }

    [Fact]
    public async Task A_rebuild_needs_a_token()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync(
            "/api/read-model/rebuild", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_rebuild_writes_both_documents_from_the_database()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = $"p-{Guid.NewGuid():N}"[..20];

        await client.PostAsJsonAsync(
            "/api/components", NewComponent(slug), TestContext.Current.CancellationToken);

        var response = await client.PostAsync(
            "/api/read-model/rebuild", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await factory.ReadModel.ReadAsync<StatusSnapshot>(
            ReadModelDocuments.Status, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Contains(snapshot.Components, c => c.Slug == slug);

        // A component nobody has checked has no state and no percentage, and the snapshot says
        // so rather than defaulting to something reassuring.
        var mine = snapshot.Components.Single(c => c.Slug == slug);
        Assert.Equal(ComponentState.Unknown, mine.State);
        Assert.Null(mine.Uptime);
        Assert.Equal(ReadModelProjection.WindowDays, mine.Days.Count);
    }
}
