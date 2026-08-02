namespace StatusPage.Domain.Tests;

/// <summary>
/// The domain refuses to be constructed into a state it cannot mean. Each of these would
/// otherwise surface much later as a component that never changes state, or a percentage
/// with no defensible value.
/// </summary>
public class GuardTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_component_that_opens_after_no_failures_at_all_is_not_a_policy(int failures)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hysteresis(failures, SuccessesToClose: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_component_that_closes_after_no_successes_at_all_is_not_a_policy(int successes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hysteresis(FailuresToOpen: 3, SuccessesToClose: successes));
    }

    [Fact]
    public void A_latency_budget_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CheckPolicy(200, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void An_interval_cannot_end_before_it_started()
    {
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(
            () => new StateInterval(ComponentState.Up, start, start.AddHours(-1)));
    }

    [Fact]
    public void An_interval_that_starts_and_ends_together_is_allowed_because_it_covers_nothing()
    {
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        var interval = new StateInterval(ComponentState.Up, start, start);

        Assert.Equal(TimeSpan.Zero, interval.ClipTo(new TimeRange(start.AddHours(-1), start.AddHours(1)), start).Duration);
    }
}
