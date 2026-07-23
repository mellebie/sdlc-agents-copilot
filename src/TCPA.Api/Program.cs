using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using System.Threading.RateLimiting;
using TCPA.Api.Filters;
using TCPA.Api.Messaging;
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

    builder.Services.AddSingleton<IMessagePublisher, KafkaMessagePublisher>();

    // Rate limiter — configured in Task 4
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("AdminReOptIn", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Request.Headers["X-Api-Key"].ToString() is { Length: > 0 } k ? k : "anonymous",
                factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.OnRejected = async (ctx, _) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.Headers.RetryAfter = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Retry after 60 seconds." });
        };
    });

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
