namespace StatusPage.Domain.Tests;

public class IncidentLifecycleTests
{
    [Theory]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Monitoring)]
    [InlineData(IncidentStatus.Monitoring, IncidentStatus.Resolved)]
    public void An_incident_moves_forward_through_the_lifecycle(IncidentStatus from, IncidentStatus to)
    {
        Assert.True(IncidentLifecycle.CanMoveTo(from, to));
    }

    [Theory]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Monitoring)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Resolved)]
    public void Skipping_a_stage_forward_is_allowed(IncidentStatus from, IncidentStatus to)
    {
        // Some outages are understood and fixed in one go, and forcing an operator to click
        // through three stages to say so would make the history less true, not more.
        Assert.True(IncidentLifecycle.CanMoveTo(from, to));
    }

    [Theory]
    [InlineData(IncidentStatus.Monitoring, IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Monitoring, IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Investigating)]
    public void An_incident_that_is_not_over_can_go_backwards(IncidentStatus from, IncidentStatus to)
    {
        // "We thought we had it and we did not" is a real thing that happens during an
        // outage, and a status page that cannot say it is a status page people stop trusting.
        Assert.True(IncidentLifecycle.CanMoveTo(from, to));
    }

    [Theory]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Monitoring)]
    public void Nothing_comes_back_out_of_resolved(IncidentStatus to)
    {
        // Resolved is terminal on purpose. Reopening rewrites what a reader was already told,
        // and the honest version of "it came back" is a second incident that links to the
        // first — which is also what the uptime arithmetic needs, since the gap between them
        // was genuinely up.
        Assert.False(IncidentLifecycle.CanMoveTo(IncidentStatus.Resolved, to));
    }

    [Fact]
    public void Resolved_stays_resolved()
    {
        Assert.True(IncidentLifecycle.CanMoveTo(IncidentStatus.Resolved, IncidentStatus.Resolved));
    }

    [Theory]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Monitoring)]
    public void An_update_that_does_not_change_the_status_is_allowed(IncidentStatus status)
    {
        // Most updates during an outage say "still working on it".
        Assert.True(IncidentLifecycle.CanMoveTo(status, status));
    }

    [Theory]
    [InlineData(IncidentStatus.Investigating, true)]
    [InlineData(IncidentStatus.Identified, true)]
    [InlineData(IncidentStatus.Monitoring, true)]
    [InlineData(IncidentStatus.Resolved, false)]
    public void Everything_short_of_resolved_is_open(IncidentStatus status, bool expected)
    {
        Assert.Equal(expected, IncidentLifecycle.IsOpen(status));
    }
}
