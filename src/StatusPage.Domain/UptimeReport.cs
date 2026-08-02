namespace StatusPage.Domain;

/// <summary>
/// What a window of history came to. The three durations are kept rather than only the ratio,
/// because "99.4%" and "99.4% of the forty minutes we actually watched" are different claims
/// and only one of them is honest.
/// </summary>
/// <param name="Measured">How much of the window was covered by any interval at all.</param>
/// <param name="Available">Measured time the component was serving, maintenance excluded.</param>
/// <param name="Maintenance">Measured time that fell inside an announced maintenance window.</param>
public readonly record struct UptimeReport(TimeSpan Measured, TimeSpan Available, TimeSpan Maintenance)
{
    /// <summary>
    /// Measured time minus announced maintenance — the time the component was expected to work.
    /// </summary>
    public TimeSpan Accountable => Measured - Maintenance;

    /// <summary>
    /// Availability, or null when nothing was accountable. Null rather than 1.0: a component
    /// nobody watched, or one that spent the whole window in announced maintenance, has not
    /// earned a perfect score and should not be shown one.
    /// </summary>
    public double? Ratio =>
        Accountable > TimeSpan.Zero ? Available.Ticks / (double)Accountable.Ticks : null;
}
