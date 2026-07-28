using System.Text.Json.Serialization;
using ConsentService.Repositories;
using ConsentService.Security;
using ConsentService.Services;
using IntakeApi.Contracts;
using IntakeApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSingleton<ICorrelationIdGenerator, GuidCorrelationIdGenerator>();
builder.Services.AddSingleton<IInboundMessageRequestValidator, InboundMessageRequestValidator>();
builder.Services.AddSingleton<IScopeMappingResolver, ScopeMappingResolver>();
builder.Services.AddSingleton<IRoutingEligibilityService, RoutingEligibilityService>();
builder.Services.AddSingleton<IConsentLookupService, InMemoryConsentLookupService>();
builder.Services.AddSingleton<IDivergenceAuditPublisher, NullDivergenceAuditPublisher>();
builder.Services.AddSingleton<IPolicyEvaluationService, PolicyEvaluationService>();
builder.Services.AddSingleton<IConsentStateRepository, InMemoryConsentStateRepository>();
builder.Services.AddSingleton<IReOptInAuthorizationPolicy, ReOptInAuthorizationPolicy>();
builder.Services.AddSingleton<IReplayProtectionService>(_ => new ReplayProtectionService(TimeSpan.FromMinutes(30)));
builder.Services.AddSingleton<IReOptInSecurityEventPublisher, NullReOptInSecurityEventPublisher>();
builder.Services.AddSingleton<IReOptInService, ReOptInService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("X-Service-Auth", out var caller) || string.IsNullOrWhiteSpace(caller))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse
        {
            Code = "UNAUTHORIZED",
            Message = "Missing required service authentication header.",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return;
    }

    await next();
});

app.MapControllers();

app.Run();

public partial class Program;
