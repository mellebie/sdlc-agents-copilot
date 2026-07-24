using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Api.Messaging;
using TCPA.Api.Models;
using TCPA.Core.Data;

namespace TCPA.Api.Controllers;

/// <summary>
/// Health check endpoint for load balancers and monitoring tools.
/// No authentication required — probes are unauthenticated by design.
/// </summary>
[ApiController]
[Route("api/v1")]
public class HealthController : ControllerBase
{
    private readonly IMessagePublisher _publisher;
    private readonly TcpaDbContext _dbContext;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IMessagePublisher publisher,
        [FromKeyedServices("primary")] TcpaDbContext dbContext,
        ILogger<HealthController> logger)
    {
        _publisher = publisher;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current health status of the API and its dependencies.
    /// Returns 200 OK when all dependencies are reachable; 503 Service Unavailable when any are degraded.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), 200)]
    [ProducesResponseType(typeof(HealthResponse), 503)]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        bool dbOk;
        try
        {
            dbOk = await _dbContext.Database.CanConnectAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            dbOk = false;
        }

        bool kafkaOk;
        try
        {
            kafkaOk = await _publisher.CheckHealthAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka health check failed");
            kafkaOk = false;
        }

        return BuildResponse(dbOk, kafkaOk);
    }

    /// <summary>
    /// Internal overload for unit testing — allows callers to supply pre-resolved health
    /// results without triggering real dependency checks. Retained because
    /// <see cref="TcpaDbContext.Database.CanConnectAsync"/> always returns <c>true</c> against
    /// InMemory providers, making the DB-degraded scenario untestable via <see cref="GetHealth"/>.
    /// </summary>
    internal Task<IActionResult> GetHealthAsync_ForTesting(bool kafkaOk, bool dbOk)
        => Task.FromResult(BuildResponse(dbOk, kafkaOk));

    private IActionResult BuildResponse(bool dbOk, bool kafkaOk)
    {
        var healthy = dbOk && kafkaOk;

        var response = new HealthResponse(
            Status: healthy ? "healthy" : "degraded",
            Checks: new HealthChecks(
                Database: dbOk ? "ok" : "degraded",
                Kafka: kafkaOk ? "ok" : "degraded"),
            Timestamp: DateTimeOffset.UtcNow);

        return healthy ? Ok(response) : StatusCode(503, response);
    }
}
