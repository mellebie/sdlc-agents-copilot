// Infrastructure/TcpaTestFactory.cs
// Source: Agent 09b (Drew) — Functional & E2E Tests
// WebApplicationFactory<Program> that replaces SQL Server DbContext registrations with an InMemory
// database and substitutes IMessagePublisher with a NSubstitute mock so functional tests run
// without any external infrastructure (no SQL Server, no Kafka, no Cool Text).

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TCPA.Api.Messaging;
using TCPA.Core.Data;

namespace TCPA.Functional.Tests.Infrastructure;

/// <summary>
/// Shared WebApplicationFactory for all TCPA functional tests.
/// <list type="bullet">
///   <item>Overrides configuration to avoid Azure Key Vault, App Configuration, and secrets requirements.</item>
///   <item>Replaces all <see cref="TcpaDbContext"/> registrations (keyed and non-keyed) with an InMemory database.</item>
///   <item>Replaces <see cref="IMessagePublisher"/> with a controllable NSubstitute mock.</item>
/// </list>
/// All test classes that share an instance of this factory via <c>IClassFixture</c> operate against the
/// same InMemory database — use unique phone numbers / message IDs per test to prevent interference.
/// </summary>
public sealed class TcpaTestFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Unique InMemory database name per factory instance so test classes using separate
    /// factories (different <c>IClassFixture</c> type parameters) cannot share state.
    /// </summary>
    private readonly string _dbName = $"tcpa-functional-{Guid.NewGuid():N}";

    /// <summary>
    /// NSubstitute mock for <see cref="IMessagePublisher"/>.
    /// Tests can call <c>Received()</c> / <c>DidNotReceive()</c> on this instance after HTTP calls.
    /// </summary>
    public IMessagePublisher MockPublisher { get; } = Substitute.For<IMessagePublisher>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ── 1. Layer test-specific configuration overrides on top of appsettings.json ──
        // Do NOT clear sources — clearing removes the ASPNETCORE_ENVIRONMENT env-var source
        // which prevents the WebApplicationFactory from hooking into the host and causes
        // "The entry point exited without ever building an IHost."
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Kafka settings — not used (IMessagePublisher is replaced with a mock below),
                // but the config keys must exist so KafkaMessagePublisher doesn't log warnings
                // before it is replaced in the DI container.
                ["Kafka:BootstrapServers"] = "localhost:9092",
                ["Kafka:Topics:Inbound"] = "inbound-messages",
                ["Kafka:Topics:Outbound"] = "outbound-messages",

                // Serilog — silence logs during test runs (keeps xUnit output clean)
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Warning",
                ["Serilog:MinimumLevel:Override:System"] = "Warning",
            });
        });

        // ── 2. Replace services ────────────────────────────────────────────────────
        builder.ConfigureServices(services =>
        {
            ReplaceDbContextWithInMemory(services);
            ReplaceMessagePublisher(services);
        });
    }

    // ─── Public seeding helper ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TcpaDbContext"/> connected to the same InMemory database that the
    /// running application uses. Tests use this to seed or inspect data without going through
    /// the HTTP API.  Caller is responsible for disposal (<c>await using var ctx = ...</c>).
    /// </summary>
    public TcpaDbContext CreateTestDbContext() => BuildInMemoryContext();

    // ─── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Removes all keyed and non-keyed <see cref="TcpaDbContext"/> descriptors registered by
    /// <c>AddTcpaCore</c> (which uses SQL Server) and replaces them with InMemory equivalents.
    /// All three registrations (keyed "primary", keyed "replica", non-keyed) share the same
    /// <see cref="_dbName"/> so they operate on the same in-process data store.
    /// </summary>
    private void ReplaceDbContextWithInMemory(IServiceCollection services)
    {
        // Remove every descriptor whose ServiceType is TcpaDbContext or its Options.
        // In .NET 8 DI, both keyed and non-keyed descriptors have ServiceType == typeof(TcpaDbContext).
        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(TcpaDbContext) ||
                d.ServiceType == typeof(DbContextOptions<TcpaDbContext>))
            .ToList();

        foreach (var d in toRemove)
            services.Remove(d);

        // Re-register all three patterns with the same InMemory database name.
        // Using Scoped lifetime (same as production) so each request gets a fresh DbContext
        // instance but they all share the same in-memory store via the database name key.
        services.AddKeyedScoped<TcpaDbContext>("primary", (_, _) => BuildInMemoryContext());
        services.AddKeyedScoped<TcpaDbContext>("replica", (_, _) => BuildInMemoryContext());
        services.AddScoped(_ => BuildInMemoryContext());
    }

    private TcpaDbContext BuildInMemoryContext()
        => new(new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    /// <summary>
    /// Replaces the production <see cref="KafkaMessagePublisher"/> with a NSubstitute mock.
    /// The mock is pre-configured to report healthy Kafka so health checks pass.
    /// Functional tests can call <c>MockPublisher.Received()</c> to verify Kafka publish calls.
    /// </summary>
    private void ReplaceMessagePublisher(IServiceCollection services)
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IMessagePublisher));
        if (existing is not null)
            services.Remove(existing);

        // Report healthy so /api/v1/health returns 200 in smoke tests
        MockPublisher.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(true);

        services.AddSingleton(MockPublisher);
    }
}
