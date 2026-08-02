namespace StatusPage.Domain;

/// <summary>Turns a component's interval history into an availability figure.</summary>
public static class Uptime
{
    /// <summary>
    /// Measures <paramref name="window"/> against a component's <paramref name="intervals"/>,
    /// discounting any <paramref name="maintenance"/> that overlaps it.
    /// </summary>
    /// <param name="intervals">The component's state history. Order does not matter.</param>
    /// <param name="maintenance">Announced windows, which come out of the denominator.</param>
    /// <param name="window">The stretch of time being reported on.</param>
    /// <param name="asOf">
    /// The moment "now" refers to. An interval that is still open ran until this instant and
    /// no further.
    /// </param>
    public static UptimeReport Measure(
        IEnumerable<StateInterval> intervals,
        IReadOnlyCollection<TimeRange> maintenance,
        TimeRange window,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(maintenance);

        var measured = TimeSpan.Zero;
        var available = TimeSpan.Zero;
        var underMaintenance = TimeSpan.Zero;

        foreach (var interval in intervals)
        {
            var covered = interval.ClipTo(window, asOf);
            if (covered.IsEmpty)
            {
                continue;
            }

            var maintained = MaintenanceWithin(covered, maintenance);

            measured += covered.Duration;
            underMaintenance += maintained;

            if (IsServing(interval.State))
            {
                available += covered.Duration - maintained;
            }
        }

        return new UptimeReport(measured, available, underMaintenance);
    }

    /// <summary>
    /// Degraded counts as serving. The component answered correctly; it was slow. Latency is a
    /// quality signal rather than an availability one, and folding it into the same number
    /// would make the number mean two things at once.
    /// </summary>
    private static bool IsServing(ComponentState state) =>
        state is ComponentState.Up or ComponentState.Degraded;

    private static TimeSpan MaintenanceWithin(TimeRange covered, IEnumerable<TimeRange> maintenance)
    {
        var total = TimeSpan.Zero;

        foreach (var window in maintenance)
        {
            total += covered.Intersect(window).Duration;
        }

        return total;
    }
}
