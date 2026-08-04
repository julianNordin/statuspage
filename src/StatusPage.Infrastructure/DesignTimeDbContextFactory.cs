using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StatusPage.Infrastructure;

/// <summary>
/// Used by <c>dotnet ef</c> at design time only. The running application builds its context
/// from configuration and a managed identity; this exists so that generating a migration does
/// not require a host project to exist first.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StatusPageDbContext>
{
    public StatusPageDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("STATUSPAGE_DESIGNTIME_CONNECTION")
            ?? "Server=localhost,1433;Database=statuspage;User Id=sa;Password=Dev_only_p4ssword!;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<StatusPageDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StatusPageDbContext(options);
    }
}
