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
        builder.UseEnvironment("Testing");
    }

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
