namespace StatusPage.Domain.Model;

/// <summary>
/// Time that was announced in advance, and therefore does not count against availability.
/// <para>
/// It comes out of the denominator rather than counting as up. An announced outage should
/// leave the figure untouched; it should not improve it, and a component that spent a whole
/// window in maintenance reports no figure rather than a perfect one.
/// </para>
/// </summary>
public class MaintenanceWindow
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public ICollection<Component> AffectedComponents { get; init; } = [];

    /// <summary>The window as the uptime arithmetic wants it.</summary>
    public TimeRange ToRange() => new(StartsAt, EndsAt);
}
