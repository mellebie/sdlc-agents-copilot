// TCPA Functional Test Factory
// Purpose: WebApplicationFactory<Program> that replaces real external dependencies
//          (SQL Server, Azure Key Vault, Azure App Configuration, CoolText HTTP client)
//          with in-process test doubles so functional tests run without real infrastructure.
// Source: Agent 09b | Tests/functional infrastructure

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.FunctionalTests.Infrastructure;

/// <summary>
/// WebApplicationFactory that wires an in-memory EF Core database, removes Azure
/// infrastructure providers, and replaces <see cref="ICoolTextClient"/> with a
/// controllable mock so functional tests run without real external dependencies.
/// </summary>
public class TcpaFunctionalTestFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Mock CoolText SMS client — test classes can configure Setup/Verify on this instance.
    /// </summary>
    public Mock<ICoolTextClient> MockCoolTextClient { get; } = new();

    /// <summary>
    /// Each factory instance gets its own isolated in-memory database so test classes
    /// using separate factory instances cannot share state.
    /// </summary>
    private readonly string _databaseName = $"tcpa-functional-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Remove all sources added by Program.cs (Key Vault, App Configuration, etc.)
            // and replace with a minimal in-memory configuration that satisfies the app's
            // startup requirements without needing real Azure endpoints.
            config.Sources.Clear();

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // API key authentication (ApiKeyAuthFilter reads Auth:ApiKey)
                ["Auth:ApiKey"] = TcpaTestConstants.ApiKey,

                // HMAC webhook secret (CoolTextWebhookValidator reads CoolText:WebhookSecret)
                ["CoolText:WebhookSecret"] = TcpaTestConstants.WebhookSecret,

                // Confirmation SMS text (ConfirmationDispatcher reads TCPA:OptOutConfirmationSmsText)
                ["TCPA:OptOutConfirmationSmsText"] = "You have opted out of all Southern Company Gas text messages.",

                // JWT authority — left empty so Program.cs skips AddJwtBearer registration
                // (the conditional guard in Program.cs: if (!string.IsNullOrEmpty(authority)))
                // This means Admin endpoint [Authorize] attributes are effectively bypassed in
                // functional tests. See AdminReOptInJourneyTests for the documented known gap.
                ["Authentication:AdminApi:Authority"] = "",
                ["Authentication:AdminApi:Audience"] = "",
                ["Authentication:AdminApi:ValidIssuer"] = "",

                // Cool Text base URL — replaced by mock so no real HTTP calls are made
                ["CoolText:BaseUrl"] = "https://test-cooltext-not-real.example.com",
                ["CoolText:TimeoutSeconds"] = "10",

                // Application registry cache TTL
                ["ApplicationRegistry:CacheTtlMinutes"] = "5",

                // Connection strings — replaced by InMemory DB in ConfigureServices below
                ["ConnectionStrings:TcpaDatabase"] = "",
                ["ConnectionStrings:AuditLogDatabase"] = "",

                // Health check dependency timeout
                ["HealthChecks:DependencyTimeoutSeconds"] = "2",

                // Required application names list — minimal set for startup validation
                ["StartupValidation:RequiredApplicationNames:0"] = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // --- Replace SQL Server DbContext with InMemory ---
            // Remove the SQL Server DbContextOptions registered by Program.cs
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TcpaDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Also remove any DbContext registration added via AddDbContext (the open generic)
            var dbContextServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(TcpaDbContext));
            if (dbContextServiceDescriptor != null)
            {
                services.Remove(dbContextServiceDescriptor);
            }

            services.AddDbContext<TcpaDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            // --- Replace ICoolTextClient with the controllable mock ---
            var coolTextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICoolTextClient));
            if (coolTextDescriptor != null)
            {
                services.Remove(coolTextDescriptor);
            }

            services.AddSingleton(MockCoolTextClient.Object);

            // --- Remove the ApplicationRegistryStartupService hosted service ---
            // This hosted service calls GetAllActiveAsync() at startup to prime the in-memory
            // cache. With an empty InMemory DB it will succeed but find nothing, which is correct
            // — tests seed data after factory creation and rely on the per-request DB fallthrough
            // in ApplicationRegistryService (cache miss → DB read) to find seeded records.
            // No removal needed: the service runs fine against InMemory DB.
        });
    }
}
