namespace StatusPage.Infrastructure.ReadModel;

/// <summary>
/// Where the read model lives. Three documents, each with one job:
/// <list type="bullet">
/// <item><description><c>config.json</c> — written by the API when an operator changes
/// something, read by the checker. Private.</description></item>
/// <item><description><c>checker-state.json</c> — the checker's working memory between runs.
/// Private.</description></item>
/// <item><description><c>status.json</c> — what the public page reads. Public.</description></item>
/// </list>
/// <para>
/// An interface because the tests need it in memory and because the local stack runs against
/// Azurite rather than Azure. Nothing above this layer knows it is blob storage.
/// </para>
/// </summary>
public interface IReadModelStore
{
    /// <summary>Reads a document, or null when it has never been written.</summary>
    Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Writes a document, replacing whatever was there.</summary>
    Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class;
}

/// <summary>The names of the three documents, in one place so nothing spells one wrong.</summary>
public static class ReadModelDocuments
{
    /// <summary>Component settings. Written by the API, read by the checker.</summary>
    public const string Config = "config.json";

    /// <summary>Hysteresis counters and last-seen times. The checker's own.</summary>
    public const string CheckerState = "checker-state.json";

    /// <summary>The public snapshot. The only thing the status page fetches.</summary>
    public const string Status = "status.json";
}
