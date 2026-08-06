using Microsoft.EntityFrameworkCore;
using StatusPage.Domain;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Tests;

/// <summary>
/// Rules the database enforces itself. Every one of these is also checked in C#, and that is
/// the point rather than a duplication: the domain check is what gives a caller a good error,
/// and the constraint is what makes the rule true of the data even when a future code path,
/// a migration script or a hand-typed UPDATE forgets to ask.
/// </summary>
[Collection(SqlServerDatabase.Name)]
public class ConstraintTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private async Task<Component> SeedComponentAsync()
    {
        var component = new Component
        {
            Id = Guid.NewGuid(),
            Name = "The API",
            Slug = $"api-{Guid.NewGuid():N}",
            TargetUrl = "https://example.com/health",
            CreatedAt = Noon,
        };

        await using var db = fixture.NewContext();
        db.Components.Add(component);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return component;
    }

    private static ComponentInterval Interval(Guid componentId, int fromHour, int? toHour) => new()
    {
        ComponentId = componentId,
        State = ComponentState.Up,
        StartedAt = Noon.AddHours(fromHour),
        EndedAt = toHour is null ? null : Noon.AddHours(toHour.Value),
    };

    private async Task AddAsync(params ComponentInterval[] intervals)
    {
        await using var db = fixture.NewContext();
        db.Intervals.AddRange(intervals);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_component_may_have_one_open_interval()
    {
        var component = await SeedComponentAsync();

        await AddAsync(Interval(component.Id, 0, null));
    }

    [Fact]
    public async Task A_component_may_not_have_two_open_intervals()
    {
        // Two rows claiming to be "the state right now" is the corruption that makes every
        // read ambiguous, and it is exactly what a crashed checker would leave behind.
        var component = await SeedComponentAsync();
        await AddAsync(Interval(component.Id, 0, null));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(Interval(component.Id, 5, null)));
    }

    [Fact]
    public async Task The_open_interval_index_refuses_a_second_one_even_with_the_trigger_disabled()
    {
        // Dropping the filtered unique index on its own broke no test, because the overlap
        // trigger catches the same insert first: two open intervals both run to the end of
        // time, so they overlap. The index would have been unproven — present, believed, and
        // never actually exercised. This disables the trigger so the index is the only thing
        // left to refuse the row.
        var component = await SeedComponentAsync();
        await AddAsync(Interval(component.Id, 0, null));

        await using var db = fixture.NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "DISABLE TRIGGER tr_component_intervals_no_overlap ON component_intervals;",
            TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => AddAsync(Interval(component.Id, 5, null)));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ENABLE TRIGGER tr_component_intervals_no_overlap ON component_intervals;",
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Two_different_components_may_each_have_their_own_open_interval()
    {
        var first = await SeedComponentAsync();
        var second = await SeedComponentAsync();

        await AddAsync(Interval(first.Id, 0, null), Interval(second.Id, 0, null));
    }

    [Fact]
    public async Task An_interval_may_not_end_before_it_started()
    {
        var component = await SeedComponentAsync();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(Interval(component.Id, 5, 2)));
    }

    [Fact]
    public async Task Two_intervals_for_one_component_may_not_overlap()
    {
        var component = await SeedComponentAsync();
        await AddAsync(Interval(component.Id, 0, 6));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(Interval(component.Id, 3, 9)));
    }

    [Fact]
    public async Task Intervals_laid_end_to_end_do_not_count_as_overlapping()
    {
        // Half-open ranges: one ends exactly where the next begins, and the shared instant
        // belongs to the later of the two.
        var component = await SeedComponentAsync();

        await AddAsync(Interval(component.Id, 0, 6));
        await AddAsync(Interval(component.Id, 6, 12));
    }

    [Fact]
    public async Task An_open_interval_may_not_overlap_a_closed_one_that_runs_past_its_start()
    {
        var component = await SeedComponentAsync();
        await AddAsync(Interval(component.Id, 0, 12));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(Interval(component.Id, 6, null)));
    }

    [Fact]
    public async Task A_component_may_not_be_stored_with_thresholds_the_domain_would_refuse()
    {
        await using var db = fixture.NewContext();
        db.Components.Add(new Component
        {
            Id = Guid.NewGuid(),
            Name = "Broken",
            Slug = $"broken-{Guid.NewGuid():N}",
            TargetUrl = "https://example.com/health",
            CreatedAt = Noon,
            FailuresToOpen = 0,
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_component_may_not_be_stored_with_a_negative_latency_budget()
    {
        await using var db = fixture.NewContext();
        db.Components.Add(new Component
        {
            Id = Guid.NewGuid(),
            Name = "Broken",
            Slug = $"broken-{Guid.NewGuid():N}",
            TargetUrl = "https://example.com/health",
            CreatedAt = Noon,
            DegradedAboveMs = -1,
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
