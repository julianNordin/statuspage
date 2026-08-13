namespace StatusPage.Domain.Model;

/// <summary>
/// Something that went wrong, as told to the people reading the status page.
/// <para>
/// An incident is a narrative, not a measurement. The intervals table already records what the
/// checker observed; this records what a human said about it, which is the part a reader
/// actually wants and the part no probe can produce.
/// </para>
/// </summary>
public class Incident
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.Investigating;

    public IncidentImpact Impact { get; set; } = IncidentImpact.Minor;

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Set when, and only when, <see cref="Status"/> is Resolved. A check enforces it.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// True when the checker opened this rather than a person. Worth keeping separate: an
    /// automatic incident with no human update is a different thing to read than one somebody
    /// wrote, and the distinction is visible on the page.
    /// </summary>
    public bool OpenedAutomatically { get; set; }

    public ICollection<IncidentUpdate> Updates { get; init; } = [];

    /// <summary>What this incident is about. At least one, enforced by the API.</summary>
    public ICollection<Component> AffectedComponents { get; init; } = [];
}

/// <summary>One thing said about an incident, at one moment.</summary>
public class IncidentUpdate
{
    public long Id { get; set; }

    public Guid IncidentId { get; set; }

    public Incident? Incident { get; set; }

    public required string Body { get; set; }

    /// <summary>The status the incident was moved to by this update, or kept at.</summary>
    public IncidentStatus Status { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    /// <summary>Null when the checker wrote it.</summary>
    public Guid? PostedByOperatorId { get; set; }

    /// <summary>Denormalised on purpose: an update should still read correctly years later.</summary>
    public string? PostedByDisplayName { get; set; }
}
