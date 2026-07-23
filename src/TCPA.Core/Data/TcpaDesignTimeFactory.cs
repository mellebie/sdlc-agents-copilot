using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TCPA.Core.Data;

/// <summary>
/// Enables `dotnet ef migrations add` to run against TCPA.Core without a running app.
/// Reads connection string from environment variable TCPA_DB or falls back to localdb.
/// </summary>
public class TcpaDesignTimeFactory : IDesignTimeDbContextFactory<TcpaDbContext>
{
    public TcpaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TCPA_DB")
            ?? "Server=(localdb)\\mssqllocaldb;Database=TcpaCompliance_Dev;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TcpaDbContext(options);
    }
}
