using Confluent.Kafka;
using Serilog;
using TCPA.Core.Extensions;
using TCPA.Core.Interfaces;
using TCPA.OutboundDispatcher.Infrastructure;
using TCPA.OutboundDispatcher.Services;
using TCPA.OutboundDispatcher.Workers;

// Bootstrap logger — captures any startup exceptions before the full Serilog pipeline is wired
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Serilog — full pipeline reads appsettings.json for sinks, enrichers, minimum levels
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/tcpa-outbound-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 90));

    // TCPA.Core: keyed/non-keyed DbContext registrations, all repositories,
    // IKeywordDetectionService (singleton), IPhoneNumberHasher (singleton)
    builder.Services.AddTcpaCore(builder.Configuration);

    // Kafka consumer — singleton, single-threaded polling in OutboundMessageWorker.ExecuteAsync
    // EnableAutoCommit = false: offsets are committed manually after successful processing
    builder.Services.AddSingleton<IConsumer<string, string>>(_ =>
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Kafka:BootstrapServers is not configured."),
            GroupId = "tcpa-outbound-dispatcher",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        return new ConsumerBuilder<string, string>(config).Build();
    });

    // Cool Text API client — typed HttpClient; 30 s timeout for external API calls
    builder.Services.AddHttpClient<ICoolTextApiClient, CoolTextApiClient>(client =>
    {
        var apiUrl = builder.Configuration["CoolText:ApiUrl"]
            ?? throw new InvalidOperationException("CoolText:ApiUrl is not configured.");
        client.BaseAddress = new Uri(apiUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // Outbound processing services — scoped so each Kafka message gets a fresh instance
    // (resolved per-message via IServiceScopeFactory inside OutboundMessageWorker)
    builder.Services.AddScoped<IOutboundGateService, OutboundGateService>();
    builder.Services.AddScoped<IOutboundSendService, OutboundSendService>();

    // Worker — runs the Kafka consume loop for the lifetime of the host process
    builder.Services.AddHostedService<OutboundMessageWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TCPA.OutboundDispatcher terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
