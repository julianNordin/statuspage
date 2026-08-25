using Microsoft.EntityFrameworkCore;
using StatusPage.Api.Contracts;
using StatusPage.Domain;
using StatusPage.Domain.Model;

namespace StatusPage.Api.Tests;

/// <summary>
/// The uptime arithmetic, read through the endpoint a reader actually hits. The domain tests
/// prove the sums; these prove the query hands the sums the right rows.
/// </summary>
[Collection(ApiUnderTest.Name)]
public class UptimeThroughTheApiTests(ApiFactory factory)
{
    private async Task<(Guid Id, string Slug)> SeedHistoryAsync(DateTimeOffset now)
    {
        var slug = $"u-{Guid.NewGuid():N}"[..20];
        var component = new Component
        {
            Id = Guid.CreateVersion7(),
            Name = "Measured service",
            Slug = slug,
            TargetUrl = "https://example.com/health",
            CreatedAt = now.AddDays(-30),
        };

        // Twenty-four hours of history: up, then six hours down, then up and still running.
        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Up, StartedAt = now.AddHours(-24), EndedAt = now.AddHours(-12),
        });
        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Down, StartedAt = now.AddHours(-12), EndedAt = now.AddHours(-6),
        });
        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Up, StartedAt = now.AddHours(-6), EndedAt = null,
        });

        await using var db = factory.NewContext();
        db.Components.Add(component);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (component.Id, slug);
    }

    private async Task<ComponentStatusResponse> ReadAsync(string slug)
    {
        var client = factory.CreateClient();
        var status = await client.GetJsonAsync<StatusResponse>(
            "/api/status", TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        var mine = status.Components.SingleOrDefault(c => c.Slug == slug);
        Assert.NotNull(mine);
        return mine;
    }

    [Fact]
    public async Task Six_hours_down_in_twenty_four_measured_is_seventy_five_percent()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, slug) = await SeedHistoryAsync(now);

        var reported = await ReadAsync(slug);

        Assert.Equal(ComponentState.Up, reported.State);
        Assert.NotNull(reported.Uptime);
        Assert.Equal(0.75, reported.Uptime.Value, 2);

        // Only twenty-four hours were observed, out of a ninety-day window. The figure is a
        // percentage of what was measured, and the endpoint says how much that was rather
        // than quietly claiming the rest.
        Assert.Equal(24.0, reported.MeasuredHours, 1);
    }

    [Fact]
    public async Task Announcing_the_outage_as_maintenance_takes_it_out_of_the_denominator()
    {
        var now = DateTimeOffset.UtcNow;
        var (componentId, slug) = await SeedHistoryAsync(now);

        Assert.Equal(0.75, (await ReadAsync(slug)).Uptime!.Value, 2);

        await using (var db = factory.NewContext())
        {
            var component = await db.Components.SingleAsync(
                c => c.Id == componentId, TestContext.Current.CancellationToken);

            var window = new MaintenanceWindow
            {
                Id = Guid.CreateVersion7(),
                Title = "Planned database work",
                StartsAt = now.AddHours(-12),
                EndsAt = now.AddHours(-6),
            };
            window.AffectedComponents.Add(component);

            db.MaintenanceWindows.Add(window);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var after = await ReadAsync(slug);

        // Eighteen hours available out of eighteen accountable. The six hours are gone from
        // the denominator, not counted as up — which is why this is 1.0 and not 24/24.
        Assert.Equal(1.0, after.Uptime!.Value, 3);
        Assert.Equal(24.0, after.MeasuredHours, 1);
    }

    [Fact]
    public async Task A_component_nobody_has_ever_checked_reports_no_figure_rather_than_a_perfect_one()
    {
        var client = await factory.CreateSignedInClientAsync(TestContext.Current.CancellationToken);
        var slug = $"n-{Guid.NewGuid():N}"[..20];

        await client.PostAsJsonAsync(
            "/api/components",
            new CreateComponentRequest
            {
                Name = "Never checked", Slug = slug, TargetUrl = "https://example.com/health",
            },
            TestContext.Current.CancellationToken);

        var reported = await ReadAsync(slug);

        Assert.Equal(ComponentState.Unknown, reported.State);
        Assert.Null(reported.Uptime);
        Assert.Equal(0.0, reported.MeasuredHours, 3);
    }
}
