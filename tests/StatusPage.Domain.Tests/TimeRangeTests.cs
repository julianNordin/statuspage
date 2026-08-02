namespace StatusPage.Domain.Tests;

public class TimeRangeTests
{
    private static DateTimeOffset At(int hour) => new(2026, 8, 10, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_range_knows_how_long_it_is()
    {
        Assert.Equal(TimeSpan.FromHours(3), new TimeRange(At(9), At(12)).Duration);
    }

    [Fact]
    public void A_range_may_be_empty_but_never_negative()
    {
        Assert.Throws<ArgumentException>(() => new TimeRange(At(12), At(9)));
    }

    [Fact]
    public void Two_overlapping_ranges_intersect_to_the_part_they_share()
    {
        var shared = new TimeRange(At(9), At(12)).Intersect(new TimeRange(At(11), At(14)));

        Assert.Equal(new TimeRange(At(11), At(12)), shared);
    }

    [Fact]
    public void A_range_wholly_inside_another_intersects_to_itself()
    {
        var inner = new TimeRange(At(10), At(11));

        Assert.Equal(inner, new TimeRange(At(9), At(12)).Intersect(inner));
    }

    [Fact]
    public void Ranges_that_do_not_meet_intersect_to_nothing()
    {
        var shared = new TimeRange(At(9), At(10)).Intersect(new TimeRange(At(11), At(12)));

        Assert.Equal(TimeSpan.Zero, shared.Duration);
    }

    [Fact]
    public void Ranges_that_merely_touch_share_no_duration()
    {
        // A range is half-open: [start, end). Touching at a boundary is not overlapping,
        // which is what keeps adjacent intervals from double-counting the instant between them.
        var shared = new TimeRange(At(9), At(10)).Intersect(new TimeRange(At(10), At(11)));

        Assert.Equal(TimeSpan.Zero, shared.Duration);
    }

    [Fact]
    public void Intersection_is_the_same_whichever_way_round_it_is_asked()
    {
        var a = new TimeRange(At(9), At(12));
        var b = new TimeRange(At(11), At(14));

        Assert.Equal(a.Intersect(b), b.Intersect(a));
    }
}
