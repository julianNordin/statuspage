using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Seeding;

/// <summary>One component a deployment should start with, as described in configuration.</summary>
/// <param name="Name">Shown on the public page.</param>
/// <param name="Slug">Stable identifier. Matching an existing one means "leave it alone".</param>
/// <param name="TargetUrl">What gets probed. Subject to the same SSRF rules as an operator-supplied URL.</param>
/// <param name="ExpectedStatusCode">The status that counts as healthy.</param>
/// <param name="DegradedAboveMs">Answered correctly but slower than this is Degraded, not Up.</param>
/// <param name="FailuresToOpen">Consecutive failures before the component is called Down.</param>
/// <param name="SuccessesToClose">Consecutive successes before it is called Up again.</param>
/// <param name="Position">Display order.</param>
public sealed record SeededComponent(
    string Name,
    string Slug,
    string TargetUrl,
    int ExpectedStatusCode = 200,
    int DegradedAboveMs = 800,
    int FailuresToOpen = 3,
    int SuccessesToClose = 2,
    int Position = 0);

/// <summary>
/// Gives a fresh deployment something to watch.
/// <para>
/// Without this a rebuild produces a status page with no components, which means the API never
/// writes a configuration document, which means the checker has nothing to do and never writes
/// a snapshot, which means the public page renders an empty shell for ever. Every piece of that
/// chain is working correctly; there is simply nothing at the top of it. The environment is
/// reproducible from Bicep in one command and it is worth very little if what it reproduces is
/// blank.
/// </para>
/// <para>
/// Matched on slug and never updated. These are starting points, not managed state: an operator
/// who retunes a threshold or removes a component entirely should not find the deployment
/// arguing with them on the next restart.
/// </para>
/// </summary>
public sealed partial class ComponentSeeder(
    StatusPageDbContext db,
    TimeProvider clock,
    ILogger<ComponentSeeder> logger)
{
    /// <summary>Adds any configured component whose slug is not already present.</summary>
    /// <returns>How many were added. Zero means every one of them already existed.</returns>
    public async Task<int> SeedAsync(
        IReadOnlyCollection<SeededComponent> components,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (components.Count == 0)
        {
            return 0;
        }

        var slugs = components.Select(c => c.Slug).ToList();
        var existing = await db.Components
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => c.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = 0;

        foreach (var seed in components.Where(c => !existing.Contains(c.Slug, StringComparer.Ordinal)))
        {
            db.Components.Add(new Component
            {
                Id = Guid.CreateVersion7(),
                Name = seed.Name,
                Slug = seed.Slug,
                TargetUrl = seed.TargetUrl,
                ExpectedStatusCode = seed.ExpectedStatusCode,
                DegradedAboveMs = seed.DegradedAboveMs,
                FailuresToOpen = seed.FailuresToOpen,
                SuccessesToClose = seed.SuccessesToClose,
                Enabled = true,
                Position = seed.Position,
                CreatedAt = clock.GetUtcNow(),
            });

            Created(logger, seed.Slug, seed.TargetUrl);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "Seeded component {Slug} watching {TargetUrl}")]
    private static partial void Created(ILogger logger, string slug, string targetUrl);
}
