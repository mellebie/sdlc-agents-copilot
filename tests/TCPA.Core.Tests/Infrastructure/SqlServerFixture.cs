using Microsoft.EntityFrameworkCore;
using TCPA.Core.Data;
using Testcontainers.MsSql;

namespace TCPA.Core.Tests.Infrastructure;

/// <summary>
/// Shared xUnit fixture that starts a SQL Server 2022 container once per collection,
/// runs all EF Core migrations, and provides a factory method for creating DbContext instances.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>Creates a new TcpaDbContext connected to the test container.</summary>
    public TcpaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;
        return new TcpaDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Apply all migrations so each test class starts from a known schema
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// xUnit collection definition that shares a single SqlServerFixture across all
/// test classes that declare [Collection("SqlServer")].
/// </summary>
[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
