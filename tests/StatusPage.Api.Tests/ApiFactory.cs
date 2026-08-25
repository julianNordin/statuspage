using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.ReadModel;
using Testcontainers.MsSql;

namespace StatusPage.Api.Tests;

/// <summary>
/// The real application, wired to a real SQL Server in a container. The only thing replaced is
/// where the database lives; every filter, binder, serializer setting and error-handling path
/// is the one that will run in production.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Touching Services starts the host, which migrates and then seeds the operators.
        // The order matters and belongs to Program, not here.
        using var scope = Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<StatusPageDbContext>();
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Default", _container.GetConnectionString());
        // There is no migrator container here, so the app migrates itself. In a deployed
        // environment that is off and a one-shot job does it, because migrating from startup
        // is a race the moment there is more than one replica.
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:Issuer", "statuspage-tests");
        builder.UseSetting("Jwt:Audience", "statuspage-tests");
        builder.UseSetting("Jwt:SigningKey", "tests-only-signing-key-0123456789abcdefghijklmnop");
        builder.UseSetting("Jwt:LifetimeMinutes", "60");
        builder.UseSetting("Operators:0:Email", OperatorEmail);
        builder.UseSetting("Operators:0:DisplayName", "Sam Operator");
        builder.UseSetting("Operators:0:Password", OperatorPassword);
        builder.UseEnvironment("Testing");

        // The API publishes config.json when the catalogue changes. These tests care that it
        // publishes, not where to — a real blob client here would mean every API test needed
        // a storage emulator to assert something about HTTP.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReadModelStore>();
            services.AddSingleton<IReadModelStore>(ReadModel);
        });
    }

    /// <summary>The read model the API writes into, so a test can read it back.</summary>
    public InMemoryReadModelStore ReadModel { get; } = new();

    /// <summary>The seeded operator every test signs in as.</summary>
    public const string OperatorEmail = "operator@example.test";

    /// <summary>
    /// Satisfies Identity's default policy — length, upper, lower, digit and a symbol. The
    /// first attempt at this used a long lowercase phrase, which the policy refused, and the
    /// seeder logged it and carried on so every sign-in returned 401 for the wrong reason.
    /// </summary>
    public const string OperatorPassword = "Tests-Only-Operator-1";

    /// <summary>A client carrying a token for the seeded operator.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/token",
            new { Email = OperatorEmail, Password = OperatorPassword },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenBody>(TestJson.Options, cancellationToken);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        return client;
    }

    private sealed record TokenBody(string AccessToken, DateTimeOffset ExpiresAt, string DisplayName);

    public StatusPageDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<StatusPageDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        return new StatusPageDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiUnderTest : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
