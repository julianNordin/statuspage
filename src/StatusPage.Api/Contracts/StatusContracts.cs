using StatusPage.Domain;

namespace StatusPage.Api.Contracts;

/// <summary>
/// The whole public read, in the shape the snapshot is written in. The public page renders
/// this and nothing else — which is why it is a single document rather than a set of
/// endpoints to fan out over.
/// </summary>
public sealed record StatusResponse(
    DateTimeOffset GeneratedAt,
    ComponentState Overall,
    IReadOnlyList<ComponentStatusResponse> Components);

/// <param name="Slug">Stable identifier; what the public page keys on.</param>
/// <param name="Name">What a reader sees.</param>
/// <param name="State">Current committed state, after hysteresis.</param>
/// <param name="Since">When the component entered its current state.</param>
/// <param name="Uptime">
/// Availability over the reported window, or null when nothing in that window was accountable —
/// a component nobody watched has not earned a score.
/// </param>
/// <param name="MeasuredHours">How much of the window was observed at all.</param>
public sealed record ComponentStatusResponse(
    string Slug,
    string Name,
    ComponentState State,
    DateTimeOffset? Since,
    double? Uptime,
    double MeasuredHours);
