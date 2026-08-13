using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatusPage.Api.Contracts;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure.Queries;

namespace StatusPage.Api.Controllers;

/// <summary>
/// Scheduled maintenance. Announced in advance, which is what earns it the right to come out
/// of the availability denominator rather than count against it.
/// </summary>
[ApiController]
[Route("api/maintenance")]
[Produces("application/json")]
public sealed class MaintenanceController(IncidentQueries incidents, TimeProvider clock) : ControllerBase
{
    private static MaintenanceResponse ToResponse(MaintenanceWindow m) => new(
        m.Id, m.Title, m.Description, m.StartsAt, m.EndsAt,
        [.. m.AffectedComponents.Select(c => c.Slug).Order(StringComparer.Ordinal)]);

    /// <summary>Windows that have not finished yet.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<MaintenanceResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<MaintenanceResponse>> Upcoming(CancellationToken cancellationToken)
    {
        var windows = await incidents
            .MaintenanceAsync(clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return [.. windows.Select(ToResponse)];
    }

    /// <summary>Schedules a window.</summary>
    [HttpPost]
    [ProducesResponseType<MaintenanceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MaintenanceResponse>> Schedule(
        ScheduleMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EndsAt <= request.StartsAt)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A maintenance window must end after it starts.");
        }

        var components = await incidents
            .ComponentsBySlugAsync(request.ComponentSlugs, cancellationToken)
            .ConfigureAwait(false);

        if (components.Count != request.ComponentSlugs.Distinct(StringComparer.Ordinal).Count())
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "One of those components does not exist.");
        }

        var window = new MaintenanceWindow
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title,
            Description = request.Description,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
        };

        foreach (var component in components)
        {
            window.AffectedComponents.Add(component);
        }

        await incidents.AddAsync(window, cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(Upcoming), null, ToResponse(window));
    }
}
