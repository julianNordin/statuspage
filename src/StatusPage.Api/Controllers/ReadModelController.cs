using Microsoft.AspNetCore.Mvc;
using StatusPage.Infrastructure.ReadModel;

namespace StatusPage.Api.Controllers;

/// <summary>
/// Rebuilding the read model from the log.
/// <para>
/// The database is authoritative and these documents are derived from it, so this is always
/// possible — which is exactly what makes it safe for the serving path never to read the
/// database at all. Without a rebuild, a divergence between the two would be permanent and
/// the only fix would be waiting for the next outage.
/// </para>
/// </summary>
[ApiController]
[Route("api/read-model")]
[Produces("application/json")]
public sealed class ReadModelController(ReadModelProjection projection, TimeProvider clock) : ControllerBase
{
    /// <summary>Rewrites config.json and status.json from what is in the database now.</summary>
    [HttpPost("rebuild")]
    [ProducesResponseType<RebuildResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<RebuildResponse> Rebuild(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await projection.WriteConfigAsync(now, cancellationToken).ConfigureAwait(false);
        var snapshot = await projection.WriteSnapshotAsync(now, cancellationToken).ConfigureAwait(false);

        return new RebuildResponse(now, snapshot.Components.Count, snapshot.Incidents.Count);
    }
}

/// <param name="RebuiltAt">When the rebuild ran.</param>
/// <param name="Components">How many components are in the new snapshot.</param>
/// <param name="Incidents">How many incidents it carries.</param>
public sealed record RebuildResponse(DateTimeOffset RebuiltAt, int Components, int Incidents);
