namespace StatusPage.Domain;

/// <summary>
/// What the checker needs to know to do its job, written by the API whenever an operator
/// changes something.
/// <para>
/// It exists so that a normal cycle touches no database at all. Azure SQL's free offer meters
/// <em>awake</em> time, and auto-pause needs sixty unbroken idle minutes; a checker reading its
/// configuration every ten minutes would never let the database sleep and would burn a month's
/// allowance in about four days. Reading configuration from a file makes the database's only
/// visitors an operator and a genuine state change, both of which are rare.
/// </para>
/// </summary>
/// <param name="GeneratedAt">When the API last wrote this.</param>
/// <param name="Components">Everything enabled, in the order a reader sees them.</param>
public sealed record CheckerConfig(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CheckerComponent> Components);

/// <summary>One component's check settings, flattened out of the database row.</summary>
/// <param name="Id">The database id, so a transition can be written without a lookup.</param>
/// <param name="Slug">Stable identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="TargetUrl">What to fetch.</param>
/// <param name="ExpectedStatusCode">The one status code that counts as healthy.</param>
/// <param name="DegradedAboveMs">Latency budget in milliseconds.</param>
/// <param name="FailuresToOpen">Consecutive worse observations required to commit.</param>
/// <param name="SuccessesToClose">Consecutive better observations required to commit.</param>
/// <param name="Position">Display order.</param>
public sealed record CheckerComponent(
    Guid Id,
    string Slug,
    string Name,
    string TargetUrl,
    int ExpectedStatusCode,
    int DegradedAboveMs,
    int FailuresToOpen,
    int SuccessesToClose,
    int Position)
{
    public CheckPolicy CheckPolicy() => new(ExpectedStatusCode, TimeSpan.FromMilliseconds(DegradedAboveMs));

    public Hysteresis Hysteresis() => new(FailuresToOpen, SuccessesToClose);
}

/// <summary>
/// The checker's working memory between runs — the run of observations currently arguing for a
/// change, and how long it is.
/// </summary>
/// <param name="GeneratedAt">When the last cycle finished.</param>
/// <param name="Components">One entry per component, keyed by slug.</param>
public sealed record CheckerMemory(
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, ComponentMemory> Components)
{
    public static CheckerMemory Empty { get; } =
        new(DateTimeOffset.MinValue, new Dictionary<string, ComponentMemory>(StringComparer.Ordinal));
}

/// <summary>What is remembered about one component.</summary>
/// <param name="Committed">The state the rest of the system believes it is in.</param>
/// <param name="Candidate">What the current run of observations argues for.</param>
/// <param name="ConsecutiveObservations">How long that run is.</param>
/// <param name="Since">When it entered the committed state.</param>
/// <param name="LastCheckedAt">When it was last looked at.</param>
/// <param name="LastLatencyMs">Last round-trip time.</param>
public sealed record ComponentMemory(
    ComponentState Committed,
    ComponentState Candidate,
    int ConsecutiveObservations,
    DateTimeOffset? Since,
    DateTimeOffset? LastCheckedAt,
    int? LastLatencyMs)
{
    public HysteresisState ToHysteresisState() =>
        new(Committed, Candidate, ConsecutiveObservations);
}

/// <summary>
/// The whole public read, in one document.
/// <para>
/// The page fetches this and nothing else — not the API, not the database it reports on. For a
/// status page that is not an optimisation but the only correct arrangement: a page served by
/// the system it describes tells you nothing at the one moment you need it to.
/// </para>
/// </summary>
/// <param name="GeneratedAt">When the checker last wrote this.</param>
/// <param name="Overall">The worst state among the components.</param>
/// <param name="Components">Every enabled component.</param>
/// <param name="Incidents">Recent incidents, newest first.</param>
/// <param name="Maintenance">Windows that have not finished.</param>
public sealed record StatusSnapshot(
    DateTimeOffset GeneratedAt,
    ComponentState Overall,
    IReadOnlyList<SnapshotComponent> Components,
    IReadOnlyList<SnapshotIncident> Incidents,
    IReadOnlyList<SnapshotMaintenance> Maintenance)
{
    public static StatusSnapshot Empty { get; } =
        new(DateTimeOffset.MinValue, ComponentState.Unknown, [], [], []);
}

/// <param name="Slug">Stable identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="State">Current committed state.</param>
/// <param name="Since">When it entered that state.</param>
/// <param name="LastLatencyMs">Last round-trip time.</param>
/// <param name="Uptime">Availability over the reported window, or null when nothing was accountable.</param>
/// <param name="MeasuredHours">How much of the window was observed at all.</param>
/// <param name="Days">One entry per day, oldest first, for the bar chart.</param>
public sealed record SnapshotComponent(
    string Slug,
    string Name,
    ComponentState State,
    DateTimeOffset? Since,
    int? LastLatencyMs,
    double? Uptime,
    double MeasuredHours,
    IReadOnlyList<SnapshotDay> Days);

/// <param name="Date">The day, in UTC.</param>
/// <param name="Uptime">Availability that day, or null if nothing was measured.</param>
/// <param name="Worst">The worst state seen that day, for colouring the bar.</param>
public sealed record SnapshotDay(DateOnly Date, double? Uptime, ComponentState Worst);

/// <param name="Id">Incident id, so the page can link to it.</param>
/// <param name="Title">Headline.</param>
/// <param name="Status">Where it has got to.</param>
/// <param name="Impact">How bad.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="ResolvedAt">When it ended, if it has.</param>
/// <param name="AffectedComponents">Slugs.</param>
/// <param name="Updates">What was said, oldest first.</param>
public sealed record SnapshotIncident(
    Guid Id,
    string Title,
    IncidentStatus Status,
    IncidentImpact Impact,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<string> AffectedComponents,
    IReadOnlyList<SnapshotUpdate> Updates);

/// <param name="Body">What was said.</param>
/// <param name="Status">The status it moved to, or kept.</param>
/// <param name="PostedAt">When.</param>
/// <param name="PostedBy">Who, or null when the checker wrote it.</param>
public sealed record SnapshotUpdate(
    string Body,
    IncidentStatus Status,
    DateTimeOffset PostedAt,
    string? PostedBy);

/// <param name="Title">Headline.</param>
/// <param name="Description">Detail, if any.</param>
/// <param name="StartsAt">When it begins.</param>
/// <param name="EndsAt">When it ends.</param>
/// <param name="AffectedComponents">Slugs.</param>
public sealed record SnapshotMaintenance(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> AffectedComponents);
