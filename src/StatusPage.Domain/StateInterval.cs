namespace StatusPage.Domain;

/// <summary>
/// A stretch of time during which a component was in one state. This — not a row per check —
/// is how history is stored.
/// <para>
/// Ninety days of one-minute checks is roughly 130,000 rows per component. Ninety days of
/// <em>transitions</em>, for a component that stayed up, is one. The information a status page
/// needs is when things changed, and sampling stores everything except that.
/// </para>
/// </summary>
/// <param name="State">What the component was during this stretch.</param>
/// <param name="StartedAt">When it entered that state.</param>
/// <param name="EndedAt">When it left. Null while the interval is still the current one.</param>
public sealed record StateInterval(ComponentState State, DateTimeOffset StartedAt, DateTimeOffset? EndedAt)
{
    /// <summary>When it left. Null while the interval is still the current one.</summary>
    public DateTimeOffset? EndedAt { get; } = NotBefore(EndedAt, StartedAt);

    public bool IsOpen => EndedAt is null;

    private static DateTimeOffset? NotBefore(DateTimeOffset? endedAt, DateTimeOffset startedAt)
    {
        if (endedAt is { } end && end < startedAt)
        {
            throw new ArgumentException(
                $"An interval cannot end ({end:O}) before it started ({startedAt:O}).", nameof(endedAt));
        }

        return endedAt;
    }

    /// <summary>
    /// The part of this interval that falls inside <paramref name="window"/>. An open interval
    /// is treated as running to <paramref name="asOf"/>, because an interval cannot have
    /// covered time that has not happened yet.
    /// </summary>
    public TimeRange ClipTo(TimeRange window, DateTimeOffset asOf)
    {
        var end = EndedAt ?? asOf;

        // An open interval whose start is already after `asOf` covers nothing rather than
        // covering backwards.
        return end < StartedAt
            ? new TimeRange(StartedAt, StartedAt).Intersect(window)
            : new TimeRange(StartedAt, end).Intersect(window);
    }
}
