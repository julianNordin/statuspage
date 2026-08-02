
namespace StatusPage.Domain.Tests;

public class CheckPolicyTests
{
    private static readonly CheckPolicy Policy = new(
        ExpectedStatusCode: 200,
        DegradedAbove: TimeSpan.FromMilliseconds(500));

    [Fact]
    public void A_response_with_the_expected_status_inside_the_latency_budget_is_up()
    {
        var outcome = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(120));

        Assert.Equal(ComponentState.Up, Policy.Observe(outcome));
    }

    [Fact]
    public void A_response_slower_than_the_budget_is_degraded_rather_than_down()
    {
        var outcome = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(900));

        Assert.Equal(ComponentState.Degraded, Policy.Observe(outcome));
    }

    [Fact]
    public void The_budget_is_inclusive_so_a_response_exactly_at_it_is_still_up()
    {
        var outcome = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(500));

        Assert.Equal(ComponentState.Up, Policy.Observe(outcome));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(404)]
    [InlineData(301)]
    [InlineData(201)]
    public void A_status_code_other_than_the_expected_one_is_down_however_fast_it_arrived(int status)
    {
        var outcome = CheckOutcome.Responded(status, TimeSpan.FromMilliseconds(5));

        Assert.Equal(ComponentState.Down, Policy.Observe(outcome));
    }

    [Fact]
    public void A_timeout_is_down()
    {
        Assert.Equal(ComponentState.Down, Policy.Observe(CheckOutcome.TimedOut(TimeSpan.FromSeconds(10))));
    }

    [Fact]
    public void A_connection_that_never_opened_is_down()
    {
        Assert.Equal(
            ComponentState.Down,
            Policy.Observe(CheckOutcome.ConnectionFailed(TimeSpan.FromMilliseconds(30))));
    }
}
