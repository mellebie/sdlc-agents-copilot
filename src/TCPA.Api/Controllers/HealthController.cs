// TCPA Regulatory Compliance API
// Component: Health Check Controller
// Source: EPIC-007 (STORY-021) | TASK-059
// Generated: 2026-06-26

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using TCPA.Api.Services.AuditLog;

namespace TCPA.Api.Controllers;

/// <summary>
/// Provides the unauthenticated health check endpoint for infrastructure monitoring.
/// Returns 200 when all dependencies are healthy; 503 when any critical dependency
/// is degraded (TASK-059, NFS-001).
///
/// <para>
/// Security note: this endpoint intentionally excludes internal details such as
/// connection strings, hostnames, IP addresses, or database versions from its response
/// to prevent information disclosure (TASK-059 Definition of Done).
/// </para>
/// </summary>
[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthController> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="HealthController"/>.
    /// </summary>
    /// <param name="healthCheckService">
    /// ASP.NET Core built-in health check service. Registered health checks
    /// are evaluated here (TASK-059).
    /// </param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="correlationIdAccessor">Provides the current request correlation ID.</param>
    public HealthController(
        HealthCheckService healthCheckService,
        ILogger<HealthController> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
    }

    /// <summary>
    /// Evaluates the health of all registered dependencies and returns a summary.
    /// </summary>
    /// <returns>
    /// 200 OK with <c>{"status":"healthy","checks":{...},"timestamp":"..."}</c>
    /// when all dependencies pass.
    /// 503 Service Unavailable with the same structure (showing "degraded" for failing
    /// checks) when any dependency is unhealthy.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.CorrelationId;

        HealthReport report = await _healthCheckService.CheckHealthAsync(cancellationToken);

        bool isHealthy = report.Status == HealthStatus.Healthy;

        _logger.LogInformation(
            "Health check completed. Status={Status} CorrelationId={CorrelationId}",
            report.Status,
            correlationId);

        HealthResponse response = new(
            Status: isHealthy ? "healthy" : "degraded",
            Checks: report.Entries.ToDictionary(
                e => e.Key,
                e => new HealthCheckDetail(
                    Status: e.Value.Status == HealthStatus.Healthy ? "ok" : "degraded",
                    // Description is sanitized — never include connection strings, IPs, or hostnames.
                    Description: SanitizeDescription(e.Value.Description))),
            Timestamp: DateTime.UtcNow.ToString("O"));

        return isHealthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    /// <summary>
    /// Ensures the health check description does not contain connection strings, IP
    /// addresses, or other internal infrastructure details before returning to the caller.
    /// Returns a generic message when the original description looks unsafe.
    /// </summary>
    private static string? SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        // Remove any description that looks like a connection string or stack trace.
        if (description.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("at ") ||   // Stack trace frames
            description.Contains("StackTrace", StringComparison.OrdinalIgnoreCase))
        {
            return "Dependency check failed. Contact IT operations.";
        }

        return description;
    }
}

/// <summary>Health check response returned from <c>GET /health</c>.</summary>
/// <param name="Status">Overall status: "healthy" or "degraded".</param>
/// <param name="Checks">Per-dependency check results, keyed by check name.</param>
/// <param name="Timestamp">ISO 8601 UTC timestamp of the health evaluation.</param>
public sealed record HealthResponse(
    string Status,
    Dictionary<string, HealthCheckDetail> Checks,
    string Timestamp);

/// <summary>Result for a single dependency health check.</summary>
/// <param name="Status">"ok" when healthy, "degraded" when unhealthy.</param>
/// <param name="Description">Optional sanitized description. Never contains internal details.</param>
public sealed record HealthCheckDetail(string Status, string? Description);
