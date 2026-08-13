using System.ComponentModel.DataAnnotations;
using StatusPage.Domain;

namespace StatusPage.Api.Contracts;

/// <summary>An incident as a reader sees it.</summary>
public sealed record IncidentResponse(
    Guid Id,
    string Title,
    IncidentStatus Status,
    IncidentImpact Impact,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    bool OpenedAutomatically,
    IReadOnlyList<string> AffectedComponents,
    IReadOnlyList<IncidentUpdateResponse> Updates);

/// <param name="Body">What was said.</param>
/// <param name="Status">The status the incident was moved to, or kept at.</param>
/// <param name="PostedAt">When.</param>
/// <param name="PostedBy">Who, or null when the checker wrote it.</param>
public sealed record IncidentUpdateResponse(
    string Body,
    IncidentStatus Status,
    DateTimeOffset PostedAt,
    string? PostedBy);

/// <summary>Declaring an incident.</summary>
public sealed record DeclareIncidentRequest
{
    [Required, StringLength(160, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required, StringLength(4000, MinimumLength = 1)]
    public required string Body { get; init; }

    public IncidentImpact Impact { get; init; } = IncidentImpact.Minor;

    /// <summary>At least one. An incident about nothing is not an incident.</summary>
    [Required, MinLength(1)]
    public required IReadOnlyList<string> ComponentSlugs { get; init; }
}

/// <summary>Adding to an incident, and possibly moving it along.</summary>
public sealed record PostIncidentUpdateRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public required string Body { get; init; }

    public required IncidentStatus Status { get; init; }
}

/// <summary>A scheduled maintenance window as a reader sees it.</summary>
public sealed record MaintenanceResponse(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> AffectedComponents);

/// <summary>Scheduling maintenance.</summary>
public sealed record ScheduleMaintenanceRequest
{
    [Required, StringLength(160, MinimumLength = 1)]
    public required string Title { get; init; }

    [StringLength(4000)]
    public string? Description { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public required DateTimeOffset EndsAt { get; init; }

    [Required, MinLength(1)]
    public required IReadOnlyList<string> ComponentSlugs { get; init; }
}
