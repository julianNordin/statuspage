using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatusPage.Api.Contracts;
using StatusPage.Domain;
using StatusPage.Infrastructure.Queries;

namespace StatusPage.Api.Controllers;

/// <summary>
/// The public read: what is happening now and how it has been going.
/// <para>
/// From Phase 10 the public page does not call this — it reads a snapshot from blob storage,
/// so that the page reporting on the system does not depend on the system. This endpoint stays
/// as the thing the snapshot is generated from, and as a way to see the current answer without
/// waiting for the next snapshot.
/// </para>
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/status")]
[Produces("application/json")]
public sealed class StatusController(StatusQueries status, TimeProvider clock) : ControllerBase
{
    /// <summary>Days of history the reported availability figure covers.</summary>
    public const int WindowDays = 90;

    /// <summary>Current state and availability for every enabled component.</summary>
    [HttpGet]
    [ProducesResponseType<StatusResponse>(StatusCodes.Status200OK)]
    public async Task<StatusResponse> Get(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = new TimeRange(now.AddDays(-WindowDays), now);

        var components = await status.ReadAsync(window, now, cancellationToken).ConfigureAwait(false);

        return new StatusResponse(
            now,
            StatusQueries.Overall(components),
            [.. components.Select(c => new ComponentStatusResponse(
                c.Slug, c.Name, c.State, c.Since, c.Uptime, c.Measured.TotalHours))]);
    }
}
