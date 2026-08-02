namespace StatusPage.Domain;

/// <summary>
/// A half-open span of time — <c>[Start, End)</c>. Half-open is the load-bearing choice:
/// two adjacent ranges that meet at an instant share no duration, so a component's intervals
/// can be laid end to end without the boundary instant being counted twice.
/// </summary>
public readonly record struct TimeRange
{
    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end < start)
        {
            throw new ArgumentException(
                $"A range cannot end ({end:O}) before it starts ({start:O}).", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public bool IsEmpty => End == Start;

    /// <summary>
    /// The part this range shares with <paramref name="other"/>. Ranges that do not meet, or
    /// that merely touch at a boundary, return an empty range rather than a negative one.
    /// </summary>
    public TimeRange Intersect(TimeRange other)
    {
        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;

        return end <= start ? new TimeRange(start, start) : new TimeRange(start, end);
    }
}
