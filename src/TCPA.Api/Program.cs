using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using TCPA.Api.Infrastructure.Configuration;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// Bootstrap logger — active before host builds, captures startup failures.
// Replaced with the full Serilog configuration once the host is built.
// ─────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("TCPA Compliance API: Starting up.");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // ─────────────────────────────────────────────────────────────────────────
    // Configuration providers
    // Order: appsettings.json → appsettings.{Environment}.json →
    //        Azure Key Vault → Azure App Configuration → Environment Variables
    // ─────────────────────────────────────────────────────────────────────────
    ConfigureConfiguration(builder);

    // ─────────────────────────────────────────────────────────────────────────
    // Serilog structured logging
    // Full configuration with PII masking enricher and Azure Log Analytics sink
    // is registered here. The bootstrap logger above is replaced.
    // ─────────────────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .WriteTo.Async(asyncConfig =>
                asyncConfig.Console(
                    outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Database context
    // Always Encrypted requires "Column Encryption Setting=Enabled" in the
    // connection string and Azure Key Vault CMK/CEK provisioned (TASK-061).
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<TcpaDbContext>(options =>
    {
        string connectionString = builder.Configuration.GetConnectionString("TcpaDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:TcpaDatabase is required but not configured. " +
                "Ensure it is set in Azure Key Vault or application configuration.");

        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);

            // Command timeout for individual queries. Compliance gate reads must be fast;
            // this timeout prevents long-running queries from blocking the gate.
            sqlOptions.CommandTimeout(30);
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // In-memory cache (used by ApplicationRegistryService — TASK-003)
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddMemoryCache();

    // ─────────────────────────────────────────────────────────────────────────
    // Application Registry — EPIC-006
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.Configure<ApplicationRegistryOptions>(
        builder.Configuration.GetSection(ApplicationRegistryOptions.SectionName));

    // Scoped DB context but the cache service must be singleton to share the cache.
    // ApplicationRegistryService gets its own DB context scope per request via the
    // scoped TcpaDbContext. Register as scoped to align with the DbContext lifetime.
    // Note: The startup hosted service runs in a scoped context created for startup.
    builder.Services.AddScoped<IApplicationRegistryService, ApplicationRegistryService>();

    // Startup hosted service: primes cache and validates registry at startup.
    // TASK-003 (cache priming) + TASK-004 (validation) + TASK-050 (presence checks).
    builder.Services.AddHostedService<ApplicationRegistryStartupService>();

    // ─────────────────────────────────────────────────────────────────────────
    // Observability — Correlation ID (CR-001: must register before middleware use)
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<CorrelationIdAccessor>();
    builder.Services.AddScoped<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>());

    // ─────────────────────────────────────────────────────────────────────────
    // Audit Log + Reporting services
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();

    // ─────────────────────────────────────────────────────────────────────────
    // ASP.NET Core controllers and API
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ─────────────────────────────────────────────────────────────────────────
    // Health checks
    // GET /health — unauthenticated, checks DB + CoolText connectivity (TASK-059)
    // ─────────────────────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<TcpaDbContext>(
            name: "tcpa-database",
            tags: new[] { "database", "ready" });

    // ─────────────────────────────────────────────────────────────────────────
    // Authentication — JWT Bearer for Admin API endpoints (/admin/)
    // Full configuration requires the IdP endpoint confirmed by TASK-024.
    // ─────────────────────────────────────────────────────────────────────────
    string adminApiAuthority = builder.Configuration["Authentication:AdminApi:Authority"] ?? string.Empty;
    string adminApiAudience = builder.Configuration["Authentication:AdminApi:Audience"] ?? string.Empty;

    if (!string.IsNullOrWhiteSpace(adminApiAuthority))
    {
        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.Authority = adminApiAuthority;
                options.Audience = adminApiAudience;
                options.RequireHttpsMetadata = true;
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("TcpaAdminPolicy", policy =>
                policy.RequireRole("tcpa.helpdesk", "tcpa.compliance_officer"))
            // CR-003: ComplianceReporting policy required by ReportingController
            .AddPolicy("ComplianceReporting", policy =>
                policy.RequireRole("tcpa.compliance_officer", "tcpa.helpdesk"));
    }
    else
    {
        Log.Warning(
            "TCPA API: Admin API authentication is not configured (Authentication:AdminApi:Authority is empty). " +
            "Admin endpoints will be unavailable until the IdP is confirmed (TASK-024). " +
            "This is expected during initial development before IdP spike completes.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Build and configure the application pipeline
    // ─────────────────────────────────────────────────────────────────────────
    WebApplication app = builder.Build();

    // Structured request logging (must be first in pipeline).
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            // Correlation ID is injected by CorrelationIdMiddleware (TASK-053)
        };
    });

    // CR-001: Correlation ID middleware — must run before any service that reads ICorrelationIdAccessor.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // CR-002: Enable request body buffering for all requests so HMAC validation in
    // InboundSmsController can re-read the body after model binding has consumed it.
    app.Use(async (context, next) =>
    {
        context.Request.EnableBuffering();
        await next();
    });

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    // Health check endpoint — no authentication required (TASK-059).
    app.MapHealthChecks("/health");

    app.MapControllers();

    // Apply pending EF Core migrations on startup in non-Production environments.
    // In Production, migrations are applied via the deployment pipeline.
    if (!app.Environment.IsProduction())
    {
        using IServiceScope scope = app.Services.CreateScope();
        TcpaDbContext dbContext = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        dbContext.Database.Migrate();
        Log.Information("TCPA API: Database migrations applied (non-Production environment).");
    }

    Log.Information("TCPA Compliance API: Startup complete. Listening for requests.");

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException and not HostAbortedException)
{
    Log.Fatal(ex, "TCPA Compliance API: Fatal startup failure. Application will not start.");
    return 1;
}
finally
{
    Log.Information("TCPA Compliance API: Shutting down.");
    await Log.CloseAndFlushAsync();
}

// ─────────────────────────────────────────────────────────────────────────────
// Local function: configure configuration providers
// ─────────────────────────────────────────────────────────────────────────────
static void ConfigureConfiguration(WebApplicationBuilder builder)
{
    // Azure Key Vault — load secrets in all environments (required for connection strings,
    // API keys, SMTP credentials). Endpoint configured in appsettings.json.
    string? keyVaultEndpoint = builder.Configuration["AzureKeyVault:Endpoint"];
    if (!string.IsNullOrWhiteSpace(keyVaultEndpoint))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultEndpoint),
            new DefaultAzureCredential());
    }
    else
    {
        Log.Warning(
            "TCPA API: AzureKeyVault:Endpoint is not configured. " +
            "Secrets will not be loaded from Key Vault. " +
            "This is expected in local development — ensure secrets are in user secrets or environment variables.");
    }

    // Azure App Configuration — dynamic log level toggle (TASK-057).
    // Polling interval: 30 seconds (balances responsiveness with API quota).
    string? appConfigEndpoint = builder.Configuration["AzureAppConfiguration:Endpoint"];
    if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
    {
        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential());
            options.ConfigureRefresh(refresh =>
            {
                refresh.Register("Logging:MinimumLevel", refreshAll: false);
                refresh.SetRefreshInterval(TimeSpan.FromSeconds(30));
            });
        });
    }
    else
    {
        Log.Information(
            "TCPA API: AzureAppConfiguration:Endpoint is not configured. " +
            "Dynamic log level toggle will not be available. " +
            "This is expected in local development.");
    }
}
