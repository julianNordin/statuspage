using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StatusPage.Infrastructure;
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

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusPageDbContext>();
        await db.Database.MigrateAsync();
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
        builder.UseSetting("Jwt:Issuer", "statuspage-tests");
        builder.UseSetting("Jwt:Audience", "statuspage-tests");
        builder.UseSetting("Jwt:SigningKey", "tests-only-signing-key-0123456789abcdefghijklmnop");
        builder.UseSetting("Jwt:LifetimeMinutes", "60");
        builder.UseSetting("Operators:0:Email", OperatorEmail);
        builder.UseSetting("Operators:0:DisplayName", "Sam Operator");
        builder.UseSetting("Operators:0:Password", OperatorPassword);
        builder.UseEnvironment("Testing");
    }

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

        var body = await response.Content.ReadFromJsonAsync<TokenBody>(cancellationToken);

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
