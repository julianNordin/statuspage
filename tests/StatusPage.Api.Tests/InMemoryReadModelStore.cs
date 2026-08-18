using System.Text.Json;
using StatusPage.Infrastructure.ReadModel;

namespace StatusPage.Api.Tests;

/// <summary>
/// The read model in a dictionary. Round-trips through JSON on purpose: a document that cannot
/// survive serialisation is broken in production and fine in a test that stores the object.
/// </summary>
public sealed class InMemoryReadModelStore : IReadModelStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    public Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class =>
        Task.FromResult(_documents.TryGetValue(name, out var json)
            ? JsonSerializer.Deserialize<T>(json, Json)
            : null);

    public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default)
        where T : class
    {
        _documents[name] = JsonSerializer.Serialize(document, Json);
        return Task.CompletedTask;
    }
}
