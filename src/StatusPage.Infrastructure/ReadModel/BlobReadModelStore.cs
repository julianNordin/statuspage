using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace StatusPage.Infrastructure.ReadModel;

/// <summary>How to reach the read model's storage account.</summary>
public sealed class ReadModelOptions
{
    public const string Section = "ReadModel";

    /// <summary>
    /// A connection string, for local development against Azurite. Left empty in a deployed
    /// environment, where <see cref="ServiceUri"/> plus a managed identity is used instead —
    /// there is no account key anywhere in that path.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>The blob endpoint, e.g. https://name.blob.core.windows.net.</summary>
    public string? ServiceUri { get; set; }

    /// <summary>Holds config.json and checker-state.json. Never public.</summary>
    public string PrivateContainer { get; set; } = "readmodel";

    /// <summary>Holds status.json, and is readable by anyone. That is the point of it.</summary>
    public string PublicContainer { get; set; } = "status";
}

/// <summary>
/// The read model in blob storage. Documents are small JSON blobs — kilobytes — so there is no
/// paging, no partial read and no cache: each write replaces the whole document.
/// </summary>
public sealed class BlobReadModelStore(BlobServiceClient client, ReadModelOptions options) : IReadModelStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // The page reads these directly, and a human reads them when something is wrong.
        WriteIndented = false,
    };

    public async Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default)
        where T : class
    {
        var blob = Container(name).GetBlobClient(name);

        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(response.Value.Content.ToMemory().Span, Json);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Never written yet. A first run has no previous snapshot and that is not an
            // error — it is the state every deployment starts in.
            return null;
        }
    }

    public async Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(document);

        var container = Container(name);
        await container.CreateIfNotExistsAsync(
            PublicAccessTypeFor(name), cancellationToken: cancellationToken).ConfigureAwait(false);

        var blob = container.GetBlobClient(name);
        var payload = JsonSerializer.SerializeToUtf8Bytes(document, Json);

        await blob.UploadAsync(
            new BinaryData(payload),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json",

                    // The page polls this. A minute is short enough that a reader sees an
                    // incident promptly and long enough that a link doing the rounds during
                    // an outage does not become the outage.
                    CacheControl = "public, max-age=60",
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private BlobContainerClient Container(string name) =>
        client.GetBlobContainerClient(
            name == ReadModelDocuments.Status ? options.PublicContainer : options.PrivateContainer);

    private static PublicAccessType PublicAccessTypeFor(string name) =>
        name == ReadModelDocuments.Status ? PublicAccessType.Blob : PublicAccessType.None;
}
