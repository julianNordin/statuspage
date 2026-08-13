namespace StatusPage.Domain.Model;

/// <summary>
/// Something the status page reports on: one user-facing service, with exactly one check
/// behind it. A component with two checks would need a rule for combining them, and every
/// such rule is a decision the reader of a status page cannot see. One check, one component.
/// </summary>
public class Component
{
    public Guid Id { get; set; }

    /// <summary>What a reader sees. "API", "Website".</summary>
    public required string Name { get; set; }

    /// <summary>Stable, url-safe, and the thing the public snapshot keys on.</summary>
    public required string Slug { get; set; }

    /// <summary>What gets fetched. Validated against the SSRF rules before it is ever stored.</summary>
    public required string TargetUrl { get; set; }

    public int ExpectedStatusCode { get; set; } = 200;

    /// <summary>The latency budget, in milliseconds. Above it, a correct response is Degraded.</summary>
    public int DegradedAboveMs { get; set; } = 500;

    public int FailuresToOpen { get; set; } = 3;

    public int SuccessesToClose { get; set; } = 2;

    /// <summary>A disabled component keeps its history and stops being checked.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where it sits in the list a reader sees. Ties break on name.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ComponentInterval> Intervals { get; init; } = [];

    public ICollection<Incident> Incidents { get; init; } = [];

    public ICollection<MaintenanceWindow> MaintenanceWindows { get; init; } = [];

    /// <summary>The classification rule this component's checks are judged by.</summary>
    public CheckPolicy CheckPolicy() =>
        new(ExpectedStatusCode, TimeSpan.FromMilliseconds(DegradedAboveMs));

    /// <summary>How many consecutive observations this component needs before it moves.</summary>
    public Hysteresis Hysteresis() => new(FailuresToOpen, SuccessesToClose);
}
