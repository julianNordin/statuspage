namespace StatusPage.Domain.Tests;

public class HysteresisTests
{
    private static readonly Hysteresis Policy = new(FailuresToOpen: 3, SuccessesToClose: 2);

    /// <summary>Feeds a sequence of observations and returns where it ended up.</summary>
    private static HysteresisState Feed(HysteresisState from, params ComponentState[] observations) =>
        observations.Aggregate(from, Policy.Advance);

    private static HysteresisState Settled(ComponentState state) =>
        Feed(HysteresisState.NeverChecked, state);

    [Fact]
    public void A_component_never_checked_before_is_unknown()
    {
        Assert.Equal(ComponentState.Unknown, HysteresisState.NeverChecked.Committed);
    }

    [Fact]
    public void The_first_observation_commits_immediately_rather_than_waiting_for_a_threshold()
    {
        var result = Feed(HysteresisState.NeverChecked, ComponentState.Down);

        Assert.Equal(ComponentState.Down, result.Committed);
    }

    [Fact]
    public void A_single_failure_does_not_move_a_component_out_of_up()
    {
        var result = Feed(Settled(ComponentState.Up), ComponentState.Down);

        Assert.Equal(ComponentState.Up, result.Committed);
    }

    [Fact]
    public void Two_consecutive_failures_are_still_not_enough_when_three_are_required()
    {
        var result = Feed(Settled(ComponentState.Up), ComponentState.Down, ComponentState.Down);

        Assert.Equal(ComponentState.Up, result.Committed);
    }

    [Fact]
    public void The_third_consecutive_failure_commits_down()
    {
        var result = Feed(
            Settled(ComponentState.Up),
            ComponentState.Down, ComponentState.Down, ComponentState.Down);

        Assert.Equal(ComponentState.Down, result.Committed);
    }

    [Fact]
    public void A_success_in_between_resets_the_failure_count()
    {
        var result = Feed(
            Settled(ComponentState.Up),
            ComponentState.Down, ComponentState.Down,
            ComponentState.Up,
            ComponentState.Down, ComponentState.Down);

        Assert.Equal(ComponentState.Up, result.Committed);
    }

    [Fact]
    public void A_single_success_does_not_bring_a_down_component_back()
    {
        var result = Feed(Settled(ComponentState.Down), ComponentState.Up);

        Assert.Equal(ComponentState.Down, result.Committed);
    }

    [Fact]
    public void The_second_consecutive_success_commits_up()
    {
        var result = Feed(Settled(ComponentState.Down), ComponentState.Up, ComponentState.Up);

        Assert.Equal(ComponentState.Up, result.Committed);
    }

    [Fact]
    public void Recovering_takes_fewer_observations_than_failing_when_the_policy_says_so()
    {
        // Three to open, two to close. A component that flaps back is trusted sooner than
        // one that flaps out, which is the asymmetry the two numbers exist to express.
        var down = Feed(
            Settled(ComponentState.Up),
            ComponentState.Down, ComponentState.Down, ComponentState.Down);
        Assert.Equal(ComponentState.Down, down.Committed);

        var back = Feed(down, ComponentState.Up, ComponentState.Up);
        Assert.Equal(ComponentState.Up, back.Committed);
    }

    [Fact]
    public void Alternating_between_two_failing_states_commits_neither()
    {
        var result = Feed(
            Settled(ComponentState.Up),
            ComponentState.Down, ComponentState.Degraded,
            ComponentState.Down, ComponentState.Degraded,
            ComponentState.Down, ComponentState.Degraded);

        Assert.Equal(ComponentState.Up, result.Committed);
    }

    [Fact]
    public void An_observation_matching_the_committed_state_clears_a_candidate_in_progress()
    {
        var result = Feed(
            Settled(ComponentState.Up),
            ComponentState.Down, ComponentState.Down,
            ComponentState.Up);

        Assert.Equal(ComponentState.Up, result.Committed);
        Assert.Equal(0, result.ConsecutiveObservations);
    }

    [Fact]
    public void Degrading_from_up_needs_the_failure_threshold_like_any_other_departure()
    {
        var twice = Feed(Settled(ComponentState.Up), ComponentState.Degraded, ComponentState.Degraded);
        Assert.Equal(ComponentState.Up, twice.Committed);

        var thrice = Feed(twice, ComponentState.Degraded);
        Assert.Equal(ComponentState.Degraded, thrice.Committed);
    }

    [Fact]
    public void Going_from_degraded_to_down_is_a_worsening_and_needs_the_failure_threshold()
    {
        var degraded = Feed(
            Settled(ComponentState.Up),
            ComponentState.Degraded, ComponentState.Degraded, ComponentState.Degraded);
        Assert.Equal(ComponentState.Degraded, degraded.Committed);

        var twoDowns = Feed(degraded, ComponentState.Down, ComponentState.Down);
        Assert.Equal(ComponentState.Degraded, twoDowns.Committed);

        Assert.Equal(ComponentState.Down, Feed(twoDowns, ComponentState.Down).Committed);
    }

    [Fact]
    public void Going_from_down_to_degraded_is_an_improvement_and_needs_only_the_success_threshold()
    {
        var down = Settled(ComponentState.Down);

        var result = Feed(down, ComponentState.Degraded, ComponentState.Degraded);

        Assert.Equal(ComponentState.Degraded, result.Committed);
    }
}
