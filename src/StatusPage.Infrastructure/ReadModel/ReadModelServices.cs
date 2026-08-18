using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StatusPage.Infrastructure.ReadModel;

/// <summary>Wires the read model into a host. Both the API and the checker use it.</summary>
public static class ReadModelServices
{
    public static IServiceCollection AddReadModel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(ReadModelOptions.Section).Get<ReadModelOptions>()
            ?? new ReadModelOptions();

        services.AddSingleton(options);

        services.AddSingleton(_ => string.IsNullOrWhiteSpace(options.ConnectionString)
            // Deployed: the blob endpoint plus a managed identity. There is no account key in
            // configuration, in Key Vault, or anywhere else — the identity is the credential.
            ? new BlobServiceClient(new Uri(options.ServiceUri!), new DefaultAzureCredential())
            // Local: Azurite, whose well-known development connection string is public by
            // design and grants nothing anywhere else.
            : new BlobServiceClient(options.ConnectionString));

        services.AddSingleton<IReadModelStore, BlobReadModelStore>();
        services.AddScoped<ReadModelProjection>();

        return services;
    }
}
