namespace StatusPage.Domain.Tests;

public class UptimeTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Hours from midnight. 24 is the following midnight, which is the point.</summary>
    private static DateTimeOffset At(int hour) => Midnight.AddHours(hour);

    private static readonly TimeRange Window = new(At(0), At(24));
    private static readonly DateTimeOffset Now = At(24);

    private static StateInterval Interval(ComponentState state, int from, int? to) =>
        new(state, At(from), to is null ? null : At(to.Value));

    private static UptimeReport Report(params StateInterval[] intervals) =>
        Uptime.Measure(intervals, [], Window, Now);

    [Fact]
    public void A_component_up_for_the_whole_window_is_fully_available()
    {
        var report = Report(Interval(ComponentState.Up, 0, 24));

        Assert.Equal(1.0, report.Ratio);
    }

    [Fact]
    public void A_component_down_for_half_the_window_is_half_available()
    {
        var report = Report(
            Interval(ComponentState.Up, 0, 12),
            Interval(ComponentState.Down, 12, 24));

        Assert.Equal(0.5, report.Ratio);
    }

    [Fact]
    public void Degraded_counts_as_available_because_the_component_was_still_serving()
    {
        var report = Report(
            Interval(ComponentState.Up, 0, 12),
            Interval(ComponentState.Degraded, 12, 24));

        Assert.Equal(1.0, report.Ratio);
    }

    [Fact]
    public void Time_no_interval_covers_is_not_measured_and_is_not_claimed()
    {
        // Only six of the window's twenty-four hours were observed at all. Reporting 100%
        // for the day would be claiming availability for eighteen hours nobody watched.
        var report = Report(Interval(ComponentState.Up, 0, 6));

        Assert.Equal(TimeSpan.FromHours(6), report.Measured);
        Assert.Equal(1.0, report.Ratio);
    }

    [Fact]
    public void A_window_with_nothing_measured_in_it_has_no_figure_at_all()
    {
        var report = Report();

        Assert.Null(report.Ratio);
        Assert.Equal(TimeSpan.Zero, report.Measured);
    }

    [Fact]
    public void An_interval_still_open_runs_to_the_end_of_the_window()
    {
        var report = Report(Interval(ComponentState.Up, 0, null));

        Assert.Equal(TimeSpan.FromHours(24), report.Measured);
    }

    [Fact]
    public void An_interval_still_open_stops_at_now_when_now_is_inside_the_window()
    {
        var report = Uptime.Measure([Interval(ComponentState.Up, 0, null)], [], Window, At(10));

        Assert.Equal(TimeSpan.FromHours(10), report.Measured);
    }

    [Fact]
    public void An_interval_starting_before_the_window_is_clipped_to_it()
    {
        var earlier = new StateInterval(ComponentState.Up, At(0).AddDays(-3), At(6));

        var report = Report(earlier);

        Assert.Equal(TimeSpan.FromHours(6), report.Measured);
    }

    [Fact]
    public void Maintenance_comes_out_of_the_denominator_rather_than_counting_as_up()
    {
        // Down for the second half, but the whole of that second half was announced
        // maintenance. Nothing unplanned happened, so availability is untouched.
        var report = Uptime.Measure(
            [Interval(ComponentState.Up, 0, 12), Interval(ComponentState.Down, 12, 24)],
            [new TimeRange(At(12), At(24))],
            Window,
            Now);

        Assert.Equal(1.0, report.Ratio);
        Assert.Equal(TimeSpan.FromHours(12), report.Maintenance);
    }

    [Fact]
    public void Maintenance_while_a_component_was_up_does_not_inflate_the_figure()
    {
        var report = Uptime.Measure(
            [Interval(ComponentState.Up, 0, 24)],
            [new TimeRange(At(0), At(12))],
            Window,
            Now);

        Assert.Equal(1.0, report.Ratio);
    }

    [Fact]
    public void An_outage_outside_a_maintenance_window_still_counts_against_the_figure()
    {
        // Six hours down, of which only three were announced. The other three are a real outage.
        var report = Uptime.Measure(
            [Interval(ComponentState.Up, 0, 18), Interval(ComponentState.Down, 18, 24)],
            [new TimeRange(At(18), At(21))],
            Window,
            Now);

        // Denominator is 24h - 3h maintenance = 21h. Available is the 18h it was up.
        Assert.Equal(18.0 / 21.0, report.Ratio!.Value, 10);
    }

    [Fact]
    public void Maintenance_outside_the_window_is_ignored()
    {
        var report = Uptime.Measure(
            [Interval(ComponentState.Up, 0, 24)],
            [new TimeRange(At(0).AddDays(-5), At(0).AddDays(-4))],
            Window,
            Now);

        Assert.Equal(TimeSpan.Zero, report.Maintenance);
        Assert.Equal(1.0, report.Ratio);
    }

    [Fact]
    public void A_window_entirely_under_maintenance_has_no_figure_rather_than_a_perfect_one()
    {
        var report = Uptime.Measure(
            [Interval(ComponentState.Down, 0, 24)],
            [new TimeRange(At(0), At(24))],
            Window,
            Now);

        Assert.Null(report.Ratio);
    }

    [Fact]
    public void Unknown_time_is_measured_but_is_not_available()
    {
        var report = Report(
            Interval(ComponentState.Unknown, 0, 12),
            Interval(ComponentState.Up, 12, 24));

        Assert.Equal(0.5, report.Ratio);
    }
}
