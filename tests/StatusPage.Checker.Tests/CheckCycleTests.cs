using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StatusPage.Checker.Probing;
using StatusPage.Domain;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure;
using Testcontainers.MsSql;

namespace StatusPage.Checker.Tests;

/// <summary>
/// A probe that answers whatever the test says, so the cycle around it can be exercised
/// without a network. Everything interesting here is a function of outcomes, and outcomes are
/// far easier to arrange than servers.
/// </summary>
internal sealed class ScriptedProbe : ITargetProbe
{
    private readonly Queue<CheckOutcome> _script = new();

    public CheckOutcome Default { get; set; } = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(10));

    public int Calls { get; private set; }

    public ScriptedProbe Then(CheckOutcome outcome, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            _script.Enqueue(outcome);
        }

        return this;
    }

    public Task<CheckOutcome> ProbeAsync(Component component, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_script.Count > 0 ? _script.Dequeue() : Default);
    }
}

public sealed class CheckerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public StatusPageDbContext NewContext() =>
        new(new DbContextOptionsBuilder<StatusPageDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options);
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

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

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

        await using var db = fixture.NewContext();

        // A cycle probes every enabled component, and these tests share one container. Each
        // test also has its own clock, so a cycle here would try to close another test's open
        // interval at an earlier instant than it started — which the ends-after-start
        // constraint refuses, correctly and confusingly. Disabling what came before makes
        // each test the only thing its own cycles can see.
        await db.Components.ExecuteUpdateAsync(
            c => c.SetProperty(x => x.Enabled, false), TestContext.Current.CancellationToken);

        db.Components.Add(component);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return component;
    }

    private async Task<CycleResult> RunAsync(ITargetProbe probe)
    {
        await using var db = fixture.NewContext();
        var cycle = new CheckCycle(db, probe, _clock, NullLogger<CheckCycle>.Instance);
        return await cycle.RunAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<ComponentInterval>> IntervalsAsync(Guid componentId)
    {
        await using var db = fixture.NewContext();
        return await db.Intervals
            .Where(i => i.ComponentId == componentId)
            .OrderBy(i => i.StartedAt).ThenBy(i => i.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_first_healthy_check_commits_up_and_writes_one_interval()
    {
        var component = await SeedAsync();

        var result = await RunAsync(new ScriptedProbe { Default = Healthy });

        Assert.Equal(1, result.Transitions);

        var intervals = await IntervalsAsync(component.Id);
        var mine = intervals.Where(i => i.ComponentId == component.Id).ToList();
        Assert.Single(mine);
        Assert.Equal(ComponentState.Up, mine[0].State);
        Assert.Null(mine[0].EndedAt);
    }

    [Fact]
    public async Task A_component_that_keeps_answering_writes_nothing_after_the_first_row()
    {
        // This is the whole point. A row per check would be ~130,000 per component per
        // quarter; a row per change is one. Running the cycle five more times must add none.
        var component = await SeedAsync();
        var probe = new ScriptedProbe { Default = Healthy };

        await RunAsync(probe);
        var afterFirst = (await IntervalsAsync(component.Id)).Count;

        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            var result = await RunAsync(probe);
            Assert.Equal(0, result.Transitions);
        }

        Assert.Equal(afterFirst, (await IntervalsAsync(component.Id)).Count);
    }

    [Fact]
    public async Task One_failure_does_not_move_a_component_that_is_up()
    {
        var component = await SeedAsync(failuresToOpen: 3);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        _clock.Advance(TimeSpan.FromMinutes(10));
        var result = await RunAsync(new ScriptedProbe { Default = Broken });

        Assert.Equal(0, result.Transitions);
        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(ComponentState.Up, intervals[^1].State);
    }

    [Fact]
    public async Task The_third_consecutive_failure_closes_the_interval_and_opens_a_down_one()
    {
        var component = await SeedAsync(failuresToOpen: 3);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            await RunAsync(new ScriptedProbe { Default = Broken });
        }

        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(2, intervals.Count);

        Assert.Equal(ComponentState.Up, intervals[0].State);
        Assert.NotNull(intervals[0].EndedAt);

        Assert.Equal(ComponentState.Down, intervals[1].State);
        Assert.Null(intervals[1].EndedAt);

        // Half-open ranges laid end to end: the old interval ends exactly where the new one
        // begins, so no instant is counted twice and none is lost.
        Assert.Equal(intervals[0].EndedAt, intervals[1].StartedAt);
    }

    [Fact]
    public async Task Going_down_opens_an_incident_and_staying_down_does_not_open_another()
    {
        var component = await SeedAsync(failuresToOpen: 2);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        for (var i = 0; i < 2; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            await RunAsync(new ScriptedProbe { Default = Broken });
        }

        await using (var db = fixture.NewContext())
        {
            var incidents = await db.Incidents
                .Include(x => x.AffectedComponents)
                .Where(x => x.AffectedComponents.Any(c => c.Id == component.Id))
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(incidents);
            Assert.True(incidents[0].OpenedAutomatically);
            Assert.Equal(IncidentStatus.Investigating, incidents[0].Status);
        }

        // Still down ten minutes later. A second incident every cycle would bury the first.
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            var result = await RunAsync(new ScriptedProbe { Default = Broken });
            Assert.Equal(0, result.IncidentsOpened);
        }

        await using (var db = fixture.NewContext())
        {
            var count = await db.Incidents
                .Where(x => x.AffectedComponents.Any(c => c.Id == component.Id))
                .CountAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task Coming_back_up_writes_an_interval_but_does_not_resolve_the_incident()
    {
        // "It answers again" and "it is fixed" are different claims, and only a person can
        // make the second one. The incident stays open for somebody to close.
        var component = await SeedAsync(failuresToOpen: 2, successesToClose: 2);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        for (var i = 0; i < 2; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            await RunAsync(new ScriptedProbe { Default = Broken });
        }

        for (var i = 0; i < 2; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            await RunAsync(new ScriptedProbe { Default = Healthy });
        }

        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(3, intervals.Count);
        Assert.Equal(ComponentState.Up, intervals[^1].State);

        await using var db = fixture.NewContext();
        var incident = await db.Incidents
            .Where(x => x.AffectedComponents.Any(c => c.Id == component.Id))
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public async Task Going_down_a_second_time_while_the_first_incident_is_still_open_adds_no_second_one()
    {
        // The already-open guard is only reachable on a transition *to* Down, and a component
        // that simply stays down never transitions again — so "still down, no new incident"
        // never touches it. Removing the guard broke no test until this one existed. A
        // component that flaps down, up, and down again does reach it.
        var component = await SeedAsync(failuresToOpen: 2, successesToClose: 2);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        async Task DriveAsync(CheckOutcome outcome, int times)
        {
            for (var i = 0; i < times; i++)
            {
                _clock.Advance(TimeSpan.FromMinutes(10));
                await RunAsync(new ScriptedProbe { Default = outcome });
            }
        }

        await DriveAsync(Broken, 2);
        await DriveAsync(Healthy, 2);
        await DriveAsync(Broken, 2);

        await using var db = fixture.NewContext();
        var incidents = await db.Incidents
            .Where(x => x.AffectedComponents.Any(c => c.Id == component.Id))
            .CountAsync(TestContext.Current.CancellationToken);

        // The first incident is still open because nobody resolved it, and an unresolved
        // incident already says what the second one would.
        Assert.Equal(1, incidents);
    }

    [Fact]
    public async Task A_slow_but_correct_response_becomes_degraded_rather_than_down()
    {
        var component = await SeedAsync(failuresToOpen: 2);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        for (var i = 0; i < 2; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            await RunAsync(new ScriptedProbe { Default = Slow });
        }

        var intervals = await IntervalsAsync(component.Id);
        Assert.Equal(ComponentState.Degraded, intervals[^1].State);

        // Degraded is not an outage, so it opens nothing.
        await using var db = fixture.NewContext();
        var incidents = await db.Incidents
            .CountAsync(x => x.AffectedComponents.Any(c => c.Id == component.Id),
                TestContext.Current.CancellationToken);

        Assert.Equal(0, incidents);
    }

    [Fact]
    public async Task A_disabled_component_is_not_checked_at_all()
    {
        var component = await SeedAsync();

        await using (var db = fixture.NewContext())
        {
            var tracked = await db.Components.SingleAsync(
                c => c.Id == component.Id, TestContext.Current.CancellationToken);
            tracked.Enabled = false;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var probe = new ScriptedProbe { Default = Healthy };
        await RunAsync(probe);

        Assert.Empty(await IntervalsAsync(component.Id));
    }

    [Fact]
    public async Task The_checker_remembers_how_long_the_current_run_is_between_cycles()
    {
        // The counter has to survive process exit, because the process exits after every
        // cycle. Two failures out of three required, across two separate runs.
        var component = await SeedAsync(failuresToOpen: 3);
        await RunAsync(new ScriptedProbe { Default = Healthy });

        _clock.Advance(TimeSpan.FromMinutes(10));
        await RunAsync(new ScriptedProbe { Default = Broken });

        await using (var db = fixture.NewContext())
        {
            var state = await db.CheckerState.SingleAsync(
                s => s.ComponentId == component.Id, TestContext.Current.CancellationToken);

            Assert.Equal(ComponentState.Down, state.Candidate);
            Assert.Equal(1, state.ConsecutiveObservations);
        }

        _clock.Advance(TimeSpan.FromMinutes(10));
        await RunAsync(new ScriptedProbe { Default = Broken });

        await using (var db = fixture.NewContext())
        {
            var state = await db.CheckerState.SingleAsync(
                s => s.ComponentId == component.Id, TestContext.Current.CancellationToken);

            Assert.Equal(2, state.ConsecutiveObservations);
        }
    }
}

/// <summary>
/// A clock the test moves by hand. Every rule in this project takes its time from an injected
/// TimeProvider, which is what makes an eight-cycle outage a few microseconds instead of
/// eighty minutes.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
