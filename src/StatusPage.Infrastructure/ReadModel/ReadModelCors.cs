using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace StatusPage.Infrastructure.ReadModel;

/// <summary>
/// Lets a browser on the front end's origin read the snapshot.
/// <para>
/// Without this the whole design does not work. The page fetches status.json directly from
/// blob storage, the page is served from a different origin, and a cross-origin read needs
/// <c>Access-Control-Allow-Origin</c> on the response — anonymous public read is not enough.
/// A public blob and a browser that refuses to look at it is exactly as useful as a private
/// one.
/// </para>
/// <para>
/// In Azure this belongs in the template that creates the storage account, and it is declared
/// there. This exists for local development and for the end-to-end suite, where there is no
/// template — Azurite starts with no CORS rules at all and there is nothing else to set them.
/// </para>
/// </summary>
public sealed partial class ReadModelCors(BlobServiceClient client, ILogger<ReadModelCors> logger)
{
    public async Task ConfigureAsync(
        IReadOnlyCollection<string> allowedOrigins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        if (allowedOrigins.Count == 0)
        {
            return;
        }

        try
        {
            var properties = await client.GetPropertiesAsync(cancellationToken).ConfigureAwait(false);

            properties.Value.Cors.Clear();
            properties.Value.Cors.Add(new BlobCorsRule
            {
                AllowedOrigins = string.Join(',', allowedOrigins),

                // GET and HEAD only. Nothing a browser does to this container should ever be
                // a write, and the account key that could authorise one is not in the page.
                AllowedMethods = "GET,HEAD",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600,
            });

            await client.SetPropertiesAsync(properties.Value, cancellationToken).ConfigureAwait(false);

            // The collection is passed rather than joined: a source-generated message
            // formats its arguments only if the level is enabled, and joining here would do
            // the work whether or not anybody reads it.
            Configured(logger, allowedOrigins);
        }
        catch (Exception ex)
        {
            // Setting service properties needs account-level permission the deployed identity
            // deliberately does not have — there it is the template's job. Failing to start
            // over it would break the environment where this is not needed at all.
            NotConfigured(logger, ex.Message);
        }
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information,
        Message = "Blob CORS allows {Origins}")]
    private static partial void Configured(ILogger logger, IReadOnlyCollection<string> origins);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Could not set blob CORS ({Reason}). In Azure this is the template's job.")]
    private static partial void NotConfigured(ILogger logger, string reason);
}
