using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StatusPage.Infrastructure.Seeding;

namespace StatusPage.Infrastructure.Tests;

[Collection(SqlServerDatabase.Name)]
public class ComponentSeederTests(SqlServerFixture fixture)
{
    /// <summary>Seeding never advances time, so a fixed reading is the whole requirement.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.FromHours(2)));

    private ComponentSeeder NewSeeder(StatusPageDbContext db) =>
        new(db, _clock, NullLogger<ComponentSeeder>.Instance);

    [Fact]
    public async Task A_deployment_with_nothing_in_it_gets_what_configuration_asks_for()
    {
        var slug = $"seed-{Guid.NewGuid():N}"[..20];

        await using var db = fixture.NewContext();
        var added = await NewSeeder(db).SeedAsync(
            [new SeededComponent("A service", slug, "https://example.com/health", Position: 3)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, added);

        var stored = await db.Components.SingleAsync(
            c => c.Slug == slug, TestContext.Current.CancellationToken);

        Assert.Equal("A service", stored.Name);
        Assert.Equal("https://example.com/health", stored.TargetUrl);
        Assert.Equal(3, stored.Position);
        Assert.True(stored.Enabled);
        Assert.Equal(_clock.GetUtcNow(), stored.CreatedAt);
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_the_second_time()
    {
        var slug = $"seed-{Guid.NewGuid():N}"[..20];
        SeededComponent[] seeds = [new("A service", slug, "https://example.com/health")];

        await using var db = fixture.NewContext();
        Assert.Equal(1, await NewSeeder(db).SeedAsync(seeds, TestContext.Current.CancellationToken));
        Assert.Equal(0, await NewSeeder(db).SeedAsync(seeds, TestContext.Current.CancellationToken));

        Assert.Equal(1, await db.Components.CountAsync(
            c => c.Slug == slug, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task What_an_operator_changed_survives_the_next_restart()
    {
        // The claim worth protecting. Seeds are a starting point, not managed state: an
        // operator who retunes a threshold, repoints a URL or disables a component entirely
        // must not find the deployment putting it back every time the API restarts.
        var slug = $"seed-{Guid.NewGuid():N}"[..20];
        SeededComponent[] seeds = [new("A service", slug, "https://example.com/health", DegradedAboveMs: 800)];

        await using var db = fixture.NewContext();
        await NewSeeder(db).SeedAsync(seeds, TestContext.Current.CancellationToken);

        var stored = await db.Components.SingleAsync(
            c => c.Slug == slug, TestContext.Current.CancellationToken);
        stored.Name = "Renamed by an operator";
        stored.TargetUrl = "https://example.com/somewhere-else";
        stored.DegradedAboveMs = 2500;
        stored.Enabled = false;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var added = await NewSeeder(db).SeedAsync(seeds, TestContext.Current.CancellationToken);

        Assert.Equal(0, added);

        var after = await db.Components.SingleAsync(
            c => c.Slug == slug, TestContext.Current.CancellationToken);

        Assert.Equal("Renamed by an operator", after.Name);
        Assert.Equal("https://example.com/somewhere-else", after.TargetUrl);
        Assert.Equal(2500, after.DegradedAboveMs);
        Assert.False(after.Enabled);
    }

    [Fact]
    public async Task Configuring_no_components_is_not_an_error()
    {
        await using var db = fixture.NewContext();
        Assert.Equal(0, await NewSeeder(db).SeedAsync([], TestContext.Current.CancellationToken));
    }
}
