using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StatusPage.Api.Controllers;

/// <summary>
/// Liveness for the container platform.
/// <para>
/// It reports a status and nothing else. There is deliberately no build timestamp, commit
/// stamp or version here: a health endpoint is a public surface, and the only thing a caller
/// needs from it is whether to keep sending traffic.
/// </para>
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Answers as long as the process is serving.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new { status = "healthy" });
}
