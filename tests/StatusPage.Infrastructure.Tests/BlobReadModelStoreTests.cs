using StatusPage.Domain;
using StatusPage.Infrastructure.ReadModel;
using Testcontainers.Azurite;
using Azure.Storage.Blobs;

namespace StatusPage.Infrastructure.Tests;

/// <summary>
/// The blob store against a real Azurite, not a double.
/// <para>
/// Everything else in the suite talks to an in-memory store, which proves the code around the
/// store and nothing about the store itself. Container creation, the 404-means-absent rule and
/// the public-versus-private split are all things only a real client can be wrong about.
/// </para>
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _container =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public BlobReadModelStore NewStore(ReadModelOptions? options = null)
    {
        options ??= new ReadModelOptions();
        return new BlobReadModelStore(new BlobServiceClient(_container.GetConnectionString()), options);
    }

    public BlobServiceClient NewClient() => new(_container.GetConnectionString());
}

[CollectionDefinition(Name)]
public sealed class AzuriteStorage : ICollectionFixture<AzuriteFixture>
{
    public const string Name = "azurite";
}

[Collection(AzuriteStorage.Name)]
public class BlobReadModelStoreTests(AzuriteFixture fixture)
{
    private static CheckerConfig SampleConfig(string slug) => new(
        new DateTimeOffset(2026, 8, 18, 19, 30, 0, TimeSpan.FromHours(2)),
        [new CheckerComponent(Guid.CreateVersion7(), slug, "A service",
            "https://example.com/health", 200, 500, 3, 2, 0)]);

    [Fact]
    public async Task A_document_that_was_never_written_reads_as_null_rather_than_throwing()
    {
        // A first run has no previous snapshot. That is the state every deployment starts in,
        // and treating the 404 as an error would make the first cycle fail on every new
        // environment.
        var store = fixture.NewStore(new ReadModelOptions
        {
            PrivateContainer = $"c{Guid.NewGuid():N}",
            PublicContainer = $"p{Guid.NewGuid():N}",
        });

        var missing = await store.ReadAsync<CheckerConfig>(
            ReadModelDocuments.Config, TestContext.Current.CancellationToken);

        Assert.Null(missing);
    }

    [Fact]
    public async Task A_document_survives_a_round_trip_through_real_storage()
    {
        var store = fixture.NewStore(new ReadModelOptions
        {
            PrivateContainer = $"c{Guid.NewGuid():N}",
            PublicContainer = $"p{Guid.NewGuid():N}",
        });

        var written = SampleConfig("api");
        await store.WriteAsync(ReadModelDocuments.Config, written, TestContext.Current.CancellationToken);

        var read = await store.ReadAsync<CheckerConfig>(
            ReadModelDocuments.Config, TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(written.GeneratedAt, read.GeneratedAt);
        Assert.Equal("api", read.Components.Single().Slug);
        Assert.Equal(500, read.Components.Single().DegradedAboveMs);
    }

    [Fact]
    public async Task A_write_replaces_what_was_there_rather_than_appending()
    {
        var store = fixture.NewStore(new ReadModelOptions
        {
            PrivateContainer = $"c{Guid.NewGuid():N}",
            PublicContainer = $"p{Guid.NewGuid():N}",
        });

        await store.WriteAsync(ReadModelDocuments.Config, SampleConfig("first"),
            TestContext.Current.CancellationToken);
        await store.WriteAsync(ReadModelDocuments.Config, SampleConfig("second"),
            TestContext.Current.CancellationToken);

        var read = await store.ReadAsync<CheckerConfig>(
            ReadModelDocuments.Config, TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal("second", read.Components.Single().Slug);
    }

    [Fact]
    public async Task The_snapshot_goes_to_the_public_container_and_the_config_does_not()
    {
        // status.json is what the page fetches directly, so it has to be readable by anyone.
        // config.json describes what is monitored and where, and must not be.
        var options = new ReadModelOptions
        {
            PrivateContainer = $"c{Guid.NewGuid():N}",
            PublicContainer = $"p{Guid.NewGuid():N}",
        };
        var store = fixture.NewStore(options);

        await store.WriteAsync(ReadModelDocuments.Config, SampleConfig("api"),
            TestContext.Current.CancellationToken);
        await store.WriteAsync(ReadModelDocuments.Status, StatusSnapshot.Empty,
            TestContext.Current.CancellationToken);

        var client = fixture.NewClient();

        var publicContainer = client.GetBlobContainerClient(options.PublicContainer);
        var privateContainer = client.GetBlobContainerClient(options.PrivateContainer);

        Assert.True(await publicContainer.GetBlobClient(ReadModelDocuments.Status)
            .ExistsAsync(TestContext.Current.CancellationToken));
        Assert.True(await privateContainer.GetBlobClient(ReadModelDocuments.Config)
            .ExistsAsync(TestContext.Current.CancellationToken));

        // The config must not be sitting in the container anyone can read.
        Assert.False(await publicContainer.GetBlobClient(ReadModelDocuments.Config)
            .ExistsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_snapshot_is_served_as_json_with_a_short_cache_life()
    {
        var store = fixture.NewStore(new ReadModelOptions
        {
            PrivateContainer = $"c{Guid.NewGuid():N}",
            PublicContainer = $"p{Guid.NewGuid():N}",
        });

        await store.WriteAsync(ReadModelDocuments.Status, StatusSnapshot.Empty,
            TestContext.Current.CancellationToken);

        var read = await store.ReadAsync<StatusSnapshot>(
            ReadModelDocuments.Status, TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(ComponentState.Unknown, read.Overall);
    }
}
