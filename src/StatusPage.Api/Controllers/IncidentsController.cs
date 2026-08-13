using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatusPage.Api.Contracts;
using StatusPage.Domain;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure.Queries;

namespace StatusPage.Api.Controllers;

/// <summary>
/// Incidents. Reading them is public — an incident history nobody can read is not a status
/// page — and writing them is not.
/// </summary>
[ApiController]
[Route("api/incidents")]
[Produces("application/json")]
public sealed class IncidentsController(IncidentQueries incidents, TimeProvider clock) : ControllerBase
{
    /// <summary>How many incidents the public history shows.</summary>
    public const int RecentCount = 25;

    private static IncidentResponse ToResponse(Incident i) => new(
        i.Id, i.Title, i.Status, i.Impact, i.StartedAt, i.ResolvedAt, i.OpenedAutomatically,
        [.. i.AffectedComponents.Select(c => c.Slug).Order(StringComparer.Ordinal)],
        [.. i.Updates.OrderBy(u => u.PostedAt)
            .Select(u => new IncidentUpdateResponse(u.Body, u.Status, u.PostedAt, u.PostedByDisplayName))]);

    /// <summary>Recent incidents, newest first.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<IncidentResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<IncidentResponse>> Recent(CancellationToken cancellationToken)
    {
        var recent = await incidents.RecentAsync(RecentCount, cancellationToken).ConfigureAwait(false);
        return [.. recent.Select(ToResponse)];
    }

    /// <summary>One incident and everything said about it.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var incident = await incidents.FindAsync(id, cancellationToken).ConfigureAwait(false);
        return incident is null ? NotFound() : ToResponse(incident);
    }

    /// <summary>Declares an incident, with its first update.</summary>
    [HttpPost]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IncidentResponse>> Declare(
        DeclareIncidentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var components = await incidents
            .ComponentsBySlugAsync(request.ComponentSlugs, cancellationToken)
            .ConfigureAwait(false);

        // Every named slug must exist. Silently dropping one would produce an incident that
        // is about less than the operator thought, which is worse than a refusal.
        if (components.Count != request.ComponentSlugs.Distinct(StringComparer.Ordinal).Count())
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "One of those components does not exist.",
                detail: "Every slug in componentSlugs must name a component that exists.");
        }

        var now = clock.GetUtcNow();

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title,
            Status = IncidentStatus.Investigating,
            Impact = request.Impact,
            StartedAt = now,
            OpenedAutomatically = false,
        };

        foreach (var component in components)
        {
            incident.AffectedComponents.Add(component);
        }

        incident.Updates.Add(new IncidentUpdate
        {
            Body = request.Body,
            Status = IncidentStatus.Investigating,
            PostedAt = now,
            PostedByOperatorId = OperatorId(),
            PostedByDisplayName = User.Identity?.Name,
        });

        await incidents.AddAsync(incident, cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(Get), new { id = incident.Id }, ToResponse(incident));
    }

    /// <summary>Adds an update, and moves the incident along if the status changed.</summary>
    [HttpPost("{id:guid}/updates")]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncidentResponse>> PostUpdate(
        Guid id,
        PostIncidentUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var incident = await incidents.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (incident is null)
        {
            return NotFound();
        }

        if (!IncidentLifecycle.CanMoveTo(incident.Status, request.Status))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An incident cannot move that way.",
                detail: $"An incident that is {incident.Status} cannot become {request.Status}. "
                      + "Resolved is final; if it happened again, declare a new incident.");
        }

        var now = clock.GetUtcNow();

        incident.Updates.Add(new IncidentUpdate
        {
            Body = request.Body,
            Status = request.Status,
            PostedAt = now,
            PostedByOperatorId = OperatorId(),
            PostedByDisplayName = User.Identity?.Name,
        });

        incident.Status = request.Status;

        // ResolvedAt and Status must agree; a check constraint refuses the row if they do not.
        incident.ResolvedAt = request.Status == IncidentStatus.Resolved ? incident.ResolvedAt ?? now : null;

        await incidents.SaveAsync(cancellationToken).ConfigureAwait(false);

        return ToResponse(incident);
    }

    private Guid? OperatorId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id
            : null;
}
