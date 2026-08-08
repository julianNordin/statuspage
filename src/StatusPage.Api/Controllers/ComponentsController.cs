using Microsoft.AspNetCore.Mvc;
using StatusPage.Api.Contracts;
using StatusPage.Domain.Model;
using StatusPage.Infrastructure.Queries;

namespace StatusPage.Api.Controllers;

/// <summary>The component catalogue. Operator-facing; the public page never calls this.</summary>
[ApiController]
[Route("api/components")]
[Produces("application/json")]
public sealed class ComponentsController(ComponentQueries components, TimeProvider clock) : ControllerBase
{
    private static ComponentResponse ToResponse(Component c) => new(
        c.Id, c.Name, c.Slug, c.TargetUrl, c.ExpectedStatusCode, c.DegradedAboveMs,
        c.FailuresToOpen, c.SuccessesToClose, c.Enabled, c.Position);

    /// <summary>Every component, in the order a reader would see them.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ComponentResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ComponentResponse>> List(CancellationToken cancellationToken)
    {
        var all = await components.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Select(ToResponse)];
    }

    /// <summary>One component, by its slug.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<ComponentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComponentResponse>> Get(string slug, CancellationToken cancellationToken)
    {
        var component = await components.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        return component is null ? NotFound() : ToResponse(component);
    }

    /// <summary>Adds a component. The slug must not already be taken.</summary>
    [HttpPost]
    [ProducesResponseType<ComponentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ComponentResponse>> Create(
        CreateComponentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Checked here for a good error, and by a unique index for the truth. Two requests
        // racing past this check land on the index, which is the one that cannot be raced.
        if (await components.SlugExistsAsync(request.Slug, cancellationToken).ConfigureAwait(false))
        {
            // Problem() rather than Conflict(new ProblemDetails(...)): the helper goes through
            // the ProblemDetailsFactory, which sets application/problem+json. Passing a
            // ProblemDetails to Conflict() serialises the same body as application/json, which
            // looks right in a browser and is wrong to a client checking the media type.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "That slug is already taken.",
                detail: $"A component with the slug '{request.Slug}' already exists.");
        }

        var component = new Component
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Slug = request.Slug,
            TargetUrl = request.TargetUrl,
            ExpectedStatusCode = request.ExpectedStatusCode,
            DegradedAboveMs = request.DegradedAboveMs,
            FailuresToOpen = request.FailuresToOpen,
            SuccessesToClose = request.SuccessesToClose,
            Enabled = request.Enabled,
            Position = request.Position,
            CreatedAt = clock.GetUtcNow(),
        };

        await components.AddAsync(component, cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(Get), new { slug = component.Slug }, ToResponse(component));
    }

    /// <summary>Replaces a component's settings. The slug is not editable.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ComponentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComponentResponse>> Update(
        Guid id,
        UpdateComponentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var component = await components.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (component is null)
        {
            return NotFound();
        }

        component.Name = request.Name;
        component.TargetUrl = request.TargetUrl;
        component.ExpectedStatusCode = request.ExpectedStatusCode;
        component.DegradedAboveMs = request.DegradedAboveMs;
        component.FailuresToOpen = request.FailuresToOpen;
        component.SuccessesToClose = request.SuccessesToClose;
        component.Enabled = request.Enabled;
        component.Position = request.Position;

        await components.SaveAsync(cancellationToken).ConfigureAwait(false);

        return ToResponse(component);
    }

    /// <summary>Removes a component and every interval recorded for it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var component = await components.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (component is null)
        {
            return NotFound();
        }

        await components.RemoveAsync(component, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
