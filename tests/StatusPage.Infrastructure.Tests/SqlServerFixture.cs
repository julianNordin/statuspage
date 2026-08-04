using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace StatusPage.Infrastructure.Tests;

/// <summary>
/// A real SQL Server in a container, migrated with the project's own migrations. Not an
/// in-memory provider: filtered unique indexes, check constraints and collation behaviour are
/// exactly the things an in-memory provider does not have, and they are what these tests exist
/// to check.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public StatusPageDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<StatusPageDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new StatusPageDbContext(options);
    }
}

/// <summary>
/// One container for the whole assembly. Starting SQL Server costs seconds, not milliseconds,
/// and every test here is happy to share it because each writes rows nobody else looks at.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerDatabase : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
