using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatusPage.Api.Tests;

/// <summary>
/// The same serialiser settings the API uses.
/// <para>
/// Reading responses with the framework defaults instead would mean the tests parse enums as
/// numbers while the API writes names — the suite would go red on a correct change, or worse,
/// green on an incorrect one.
/// </para>
/// </summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<T?> ReadJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken) =>
        content.ReadFromJsonAsync<T>(Options, cancellationToken);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string url, CancellationToken cancellationToken) =>
        client.GetFromJsonAsync<T>(url, Options, cancellationToken);
}
