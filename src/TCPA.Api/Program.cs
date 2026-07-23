using Serilog;
using TCPA.Api.Filters;
using TCPA.Core.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services));

    builder.Services.AddTcpaCore(builder.Configuration);

    builder.Services.AddScoped<ApiKeyAuthFilter>();
    builder.Services.AddScoped<AdminApiKeyAuthFilter>();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "TCPA Compliance API", Version = "v1" });
        c.AddSecurityDefinition("ApiKey", new()
        {
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "X-Api-Key",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
        });
    });

    // Rate limiter — configured in Task 4
    builder.Services.AddRateLimiter(_ => { });

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TCPA.Api terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Required for WebApplicationFactory in tests
public partial class Program { }
