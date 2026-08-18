using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StatusPage.Domain;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.ReadModel;
using Testcontainers.MsSql;

namespace StatusPage.Checker.Tests;

public sealed class CheckerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CommandCounter Counter { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext(counted: false);
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public StatusPageDbContext NewContext(bool counted = true)
    {
        var builder = new DbContextOptionsBuilder<StatusPageDbContext>()
            .UseSqlServer(_container.GetConnectionString());

        if (counted)
        {
            builder.AddInterceptors(Counter);
        }

        return new StatusPageDbContext(builder.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class CheckerDatabase : ICollectionFixture<CheckerFixture>
{
    public const string Name = "checker";
}

[Collection(CheckerDatabase.Name)]
public class CheckCycleTests(CheckerFixture fixture)
{
    private static readonly CheckOutcome Healthy = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(20));
    private static readonly CheckOutcome Broken = CheckOutcome.Responded(500, TimeSpan.FromMilliseconds(20));
    private static readonly CheckOutcome Slow = CheckOutcome.Responded(200, TimeSpan.FromSeconds(3));

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryReadModelStore _store = new();

    /// <summary>Adds a component and writes the configuration document the checker reads.</summary>
    private async Task<Component> SeedAsync(int failuresToOpen = 3, int successesToClose = 2)
    {
        var component = new Component
        {
            Id = Guid.CreateVersion7(),
            Name = "A service",
            Slug = $"s-{Guid.NewGuid():N}"[..20],
            TargetUrl = "https://example.com/health",
            FailuresToOpen = failuresToOpen,
            SuccessesToClose = successesToClose,
            CreatedAt = _clock.GetUtcNow(),
        };

        await using var db = fixture.NewContext(counted: false);

        // A cycle works from the configuration document, and these tests share one container
        // with their own clocks. Disabling what came before keeps each test the only thing in
        // its own config.
        await db.Components.ExecuteUpdateAsync(
            c => c.SetProperty(x => x.Enabled, false), TestContext.Current.CancellationToken);

        db.Components.Add(component);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new ReadModelProjection(db, _store)
            .WriteConfigAsync(_clock.GetUtcNow(), TestContext.Current.CancellationToken);

        return component;
    }

    private async Task<CycleResult> RunAsync(CheckOutcome outcome)
    {
        await using var db = fixture.NewContext();
        var cycle = new CheckCycle(
            db,
            _store,
            new ReadModelProjection(db, _store),
            new ScriptedProbe { Default = outcome },
            _clock,
            NullLogger<CheckCycle>.Instance);

        return await cycle.RunAsync(TestContext.Current.CancellationToken);
    }

    private async Task<CycleResult> DriveAsync(CheckOutcome outcome, int times)
    {
        CycleResult? last = null;
        for (var i = 0; i < times; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            last = await RunAsync(outcome);
        }

        return last!;
    }

    private async Task<List<ComponentInterval>> IntervalsAsync(Guid componentId)
    {
        await using var db = fixture.NewContext(counted: false);
        return await db.Intervals
            .Where(i => i.ComponentId == componentId)
            .OrderBy(i => i.StartedAt).ThenBy(i => i.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_first_healthy_check_commits_up_and_writes_one_interval()
    {
        var component = await SeedAsync();

        var result = await RunAsync(Healthy);

        Assert.Equal(1, result.Transitions);
        Assert.True(result.TouchedDatabase);

        var intervals = await IntervalsAsync(component.Id);
        Assert.Single(intervals);
        Assert.Equal(ComponentState.Up, intervals[0].State);
        Assert.Null(intervals[0].EndedAt);
    }

    [Fact]
    public async Task A_quiet_cycle_sends_no_sql_at_all()
    {
        // The claim the whole read model exists for. Azure SQL's free offer meters awake time
        // and auto-pause needs sixty unbroken idle minutes, so a cycle that read its
        // configuration from the database every ten minutes would never let it sleep.
        //
        // Counted at the connection rather than trusted from a flag the code sets about itself.
        var component = await SeedAsync();
        await RunAsync(Healthy);

        fixture.Counter.Reset();

        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            var result = await RunAsync(Healthy);

            Assert.Equal(0, result.Transitions);
            Assert.False(result.TouchedDatabase);
        }

        Assert.Equal(0, fixture.Counter.Commands);
        Assert.Single(await IntervalsAsync(component.Id));
    }

    [Fact]
    public async Task A_transition_is_the_only_thing_that_reaches_the_database()
    {
        await SeedAsync(failuresToOpen: 2);
        await RunAsync(Healthy);

        fixture.Counter.Reset();

        // One failure is not enough to move it, so nothing is written.
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False((await RunAsync(Broken)).TouchedDatabase);
        Assert.Equal(0, fixture.Counter.Commands);

        // The second one moves it, and that is when SQL hears about it.
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True((await RunAsync(Broken)).TouchedDatabase);
        Assert.True(fixture.Counter.Commands > 0);
    }

    [Fact]
    public async Task One_failure_does_not_move_a_component_that_is_up()
    {
        var component = await SeedAsync(failuresToOpen: 3);
        await RunAsync(Healthy);

        var result = await DriveAsync(Broken, 1);

        Assert.Equal(0, result.Transitions);
        Assert.Equal(ComponentState.Up, (await IntervalsAsync(component.Id))[^1].State);
    }

    [Fact]
    public async Task The_third_consecutive_failure_closes_the_interval_and_opens_a_down_one()
    {
        var component = await SeedAsync(failuresToOpen: 3);
        await RunAsync(Healthy);

        await DriveAsync(Broken, 3);

        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(2, intervals.Count);

        Assert.Equal(ComponentState.Up, intervals[0].State);
        Assert.NotNull(intervals[0].EndedAt);
        Assert.Equal(ComponentState.Down, intervals[1].State);
        Assert.Null(intervals[1].EndedAt);

        // Half-open ranges laid end to end: no instant counted twice, none lost.
        Assert.Equal(intervals[0].EndedAt, intervals[1].StartedAt);
    }

    [Fact]
    public async Task Going_down_opens_an_incident_and_staying_down_does_not_open_another()
    {
        var component = await SeedAsync(failuresToOpen: 2);
        await RunAsync(Healthy);
        await DriveAsync(Broken, 2);

        await using (var db = fixture.NewContext(counted: false))
        {
            var incidents = await db.Incidents
                .Where(x => x.AffectedComponents.Any(c => c.Id == component.Id))
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(incidents);
            Assert.True(incidents[0].OpenedAutomatically);
        }

        var later = await DriveAsync(Broken, 3);
        Assert.Equal(0, later.IncidentsOpened);
    }

    [Fact]
    public async Task Going_down_a_second_time_while_the_first_incident_is_still_open_adds_no_second_one()
    {
        // The already-open guard sits inside the transition branch, so a component that merely
        // stays down never reaches it. What does is one that flaps: down, up, down.
        var component = await SeedAsync(failuresToOpen: 2, successesToClose: 2);
        await RunAsync(Healthy);

        await DriveAsync(Broken, 2);
        await DriveAsync(Healthy, 2);
        await DriveAsync(Broken, 2);

        await using var db = fixture.NewContext(counted: false);
        var incidents = await db.Incidents
            .CountAsync(x => x.AffectedComponents.Any(c => c.Id == component.Id),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, incidents);
    }

    [Fact]
    public async Task Coming_back_up_writes_an_interval_but_does_not_resolve_the_incident()
    {
        var component = await SeedAsync(failuresToOpen: 2, successesToClose: 2);
        await RunAsync(Healthy);

        await DriveAsync(Broken, 2);
        await DriveAsync(Healthy, 2);

        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(3, intervals.Count);
        Assert.Equal(ComponentState.Up, intervals[^1].State);

        await using var db = fixture.NewContext(counted: false);
        var incident = await db.Incidents
            .SingleAsync(x => x.AffectedComponents.Any(c => c.Id == component.Id),
                TestContext.Current.CancellationToken);

        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public async Task A_slow_but_correct_response_becomes_degraded_rather_than_down()
    {
        var component = await SeedAsync(failuresToOpen: 2);
        await RunAsync(Healthy);

        await DriveAsync(Slow, 2);

        Assert.Equal(ComponentState.Degraded, (await IntervalsAsync(component.Id))[^1].State);

        await using var db = fixture.NewContext(counted: false);
        Assert.Equal(0, await db.Incidents.CountAsync(
            x => x.AffectedComponents.Any(c => c.Id == component.Id),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_checker_remembers_the_run_between_cycles_without_a_database()
    {
        // The counter has to survive process exit, because the process exits after every
        // cycle. It lives in the read model, so remembering costs no database time.
        await SeedAsync(failuresToOpen: 3);
        await RunAsync(Healthy);

        await DriveAsync(Broken, 1);
        var afterOne = await _store.ReadAsync<CheckerMemory>(ReadModelDocuments.CheckerState, TestContext.Current.CancellationToken);
        Assert.NotNull(afterOne);
        var first = afterOne.Components.Values.Single();
        Assert.Equal(ComponentState.Down, first.Candidate);
        Assert.Equal(1, first.ConsecutiveObservations);

        await DriveAsync(Broken, 1);
        var afterTwo = await _store.ReadAsync<CheckerMemory>(ReadModelDocuments.CheckerState, TestContext.Current.CancellationToken);
        Assert.NotNull(afterTwo);
        Assert.Equal(2, afterTwo.Components.Values.Single().ConsecutiveObservations);
    }

    [Fact]
    public async Task A_component_missing_from_the_configuration_is_not_checked()
    {
        var component = await SeedAsync();

        await using (var db = fixture.NewContext(counted: false))
        {
            await db.Components
                .Where(c => c.Id == component.Id)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Enabled, false),
                    TestContext.Current.CancellationToken);

            await new ReadModelProjection(db, _store)
                .WriteConfigAsync(_clock.GetUtcNow(), TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(Healthy);

        Assert.Equal(0, result.Probed);
        Assert.Empty(await IntervalsAsync(component.Id));
    }

    [Fact]
    public async Task The_snapshot_the_page_reads_is_rebuilt_whenever_state_changes()
    {
        var component = await SeedAsync(failuresToOpen: 2);
        await RunAsync(Healthy);

        var up = await _store.ReadAsync<StatusSnapshot>(ReadModelDocuments.Status, TestContext.Current.CancellationToken);
        Assert.NotNull(up);
        Assert.Equal(ComponentState.Up, up.Overall);
        Assert.Equal(component.Slug, up.Components.Single().Slug);
        Assert.Equal(ReadModelProjection.WindowDays, up.Components.Single().Days.Count);

        await DriveAsync(Broken, 2);

        var down = await _store.ReadAsync<StatusSnapshot>(ReadModelDocuments.Status, TestContext.Current.CancellationToken);
        Assert.NotNull(down);
        Assert.Equal(ComponentState.Down, down.Overall);

        // The snapshot carries recent incidents across the whole page, and sibling tests in
        // this collection have their own. Only this component's is this test's business.
        Assert.Single(down.Incidents, i => i.AffectedComponents.Contains(component.Slug));
    }

    [Fact]
    public async Task A_quiet_cycle_still_freshens_the_snapshot_so_the_page_does_not_look_stale()
    {
        await SeedAsync();
        await RunAsync(Healthy);

        var before = await _store.ReadAsync<StatusSnapshot>(ReadModelDocuments.Status, TestContext.Current.CancellationToken);
        Assert.NotNull(before);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await RunAsync(Healthy);

        var after = await _store.ReadAsync<StatusSnapshot>(ReadModelDocuments.Status, TestContext.Current.CancellationToken);
        Assert.NotNull(after);

        // A reader looking at a page whose timestamp stopped moving cannot tell a quiet system
        // from a dead checker.
        Assert.True(after.GeneratedAt > before.GeneratedAt);
        Assert.Equal(ComponentState.Up, after.Overall);
    }
}
