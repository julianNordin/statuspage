using System.Net;
using StatusPage.Api.Contracts;
using StatusPage.Domain;

namespace StatusPage.Api.Tests;

[Collection(ApiUnderTest.Name)]
public class IncidentEndpointTests(ApiFactory factory)
{
    private static async Task<string> SeedComponentAsync(HttpClient client)
    {
        var slug = $"c-{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync(
            "/api/components",
            new CreateComponentRequest
            {
                Name = "A service",
                Slug = slug,
                TargetUrl = "https://example.com/health",
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        return slug;
    }

    [Fact]
    public async Task A_declared_incident_is_readable_by_anyone()
    {
        var operatorClient = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = await SeedComponentAsync(operatorClient);

        var declared = await operatorClient.PostAsJsonAsync(
            "/api/incidents",
            new DeclareIncidentRequest
            {
                Title = "Elevated error rates",
                Body = "We are looking into it.",
                Impact = IncidentImpact.Major,
                ComponentSlugs = [slug],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, declared.StatusCode);

        var incident = await declared.Content.ReadFromJsonAsync<IncidentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(incident);
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Null(incident.ResolvedAt);
        Assert.False(incident.OpenedAutomatically);
        Assert.Single(incident.Updates);

        // An incident history nobody can read is not a status page.
        var anonymous = factory.CreateClient();
        var fetched = await anonymous.GetAsync(
            $"/api/incidents/{incident.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Declaring_an_incident_needs_a_token()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/incidents",
            new DeclareIncidentRequest
            {
                Title = "Made up", Body = "Made up", ComponentSlugs = ["anything"],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_incident_about_a_component_that_does_not_exist_is_refused()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new DeclareIncidentRequest
            {
                Title = "About nothing",
                Body = "Nothing",
                ComponentSlugs = ["no-such-component"],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_incident_walks_to_resolved_and_then_will_not_move_again()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = await SeedComponentAsync(client);

        var declared = await client.PostAsJsonAsync(
            "/api/incidents",
            new DeclareIncidentRequest
            {
                Title = "Something broke", Body = "Looking.", ComponentSlugs = [slug],
            },
            TestContext.Current.CancellationToken);
        var incident = await declared.Content.ReadFromJsonAsync<IncidentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(incident);

        var resolved = await client.PostAsJsonAsync(
            $"/api/incidents/{incident.Id}/updates",
            new PostIncidentUpdateRequest { Body = "Fixed.", Status = IncidentStatus.Resolved },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var after = await resolved.Content.ReadFromJsonAsync<IncidentResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal(IncidentStatus.Resolved, after.Status);
        Assert.NotNull(after.ResolvedAt);

        // Resolved is terminal. "It came back" is a new incident, which keeps the history
        // honest and keeps the gap between them counted as the up time it was.
        var reopened = await client.PostAsJsonAsync(
            $"/api/incidents/{incident.Id}/updates",
            new PostIncidentUpdateRequest { Body = "It is back.", Status = IncidentStatus.Investigating },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);
    }

    [Fact]
    public async Task A_maintenance_window_that_ends_before_it_starts_is_refused()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = await SeedComponentAsync(client);
        var now = DateTimeOffset.UtcNow;

        var response = await client.PostAsJsonAsync(
            "/api/maintenance",
            new ScheduleMaintenanceRequest
            {
                Title = "Backwards",
                StartsAt = now.AddHours(4),
                EndsAt = now.AddHours(1),
                ComponentSlugs = [slug],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scheduled_maintenance_is_public_and_lists_what_it_affects()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = await SeedComponentAsync(client);
        var now = DateTimeOffset.UtcNow;

        var scheduled = await client.PostAsJsonAsync(
            "/api/maintenance",
            new ScheduleMaintenanceRequest
            {
                Title = "Database upgrade",
                Description = "Expect a short outage.",
                StartsAt = now.AddDays(1),
                EndsAt = now.AddDays(1).AddHours(2),
                ComponentSlugs = [slug],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);

        var anonymous = factory.CreateClient();
        var upcoming = await anonymous.GetFromJsonAsync<List<MaintenanceResponse>>(
            "/api/maintenance", TestContext.Current.CancellationToken);

        Assert.NotNull(upcoming);
        var mine = upcoming.Find(m => m.AffectedComponents.Contains(slug));
        Assert.NotNull(mine);
        Assert.Equal("Database upgrade", mine.Title);
    }
}
