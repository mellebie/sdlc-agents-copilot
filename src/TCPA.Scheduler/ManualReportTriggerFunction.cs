// TCPA Regulatory Compliance API — Azure Functions Scheduler
// Component: Manual Report Re-Run HTTP Trigger
// Source: EPIC-005 (STORY-016) | SPEC-013 | TASK-047 (AC-005)
// Generated: 2026-06-26

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.Reporting;

namespace TCPA.Scheduler;

/// <summary>
/// HTTP-triggered Azure Function that allows authorized callers to regenerate
/// a weekly compliance report for any arbitrary period (TASK-047 AC-005).
///
/// <para>
/// Authentication: Requires the same RBAC policy as the Admin API
/// (<c>tcpa.compliance_officer</c> or <c>tcpa.reporting</c> role).
/// The Azure Functions runtime must be configured with Azure AD authentication.
/// </para>
///
/// <para>
/// Idempotency: Running this function for the same period produces the same report
/// output on every invocation. The email is re-dispatched each time it is called.
/// </para>
///
/// <para>
/// Request format: <c>POST /api/reports/manual-run</c> with JSON body:
/// <code>
/// { "period_start": "2026-06-15", "period_end": "2026-06-21" }
/// </code>
/// Both fields are required. ISO 8601 dates only. period_end must be >= period_start.
/// Maximum period is 31 days to prevent accidental over-generation.
/// </para>
/// </summary>
public sealed class ManualReportTriggerFunction
{
    private readonly WeeklyComplianceReportFunction _reportFunction;
    private readonly ILogger<ManualReportTriggerFunction> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>Maximum period a manual re-run can cover (31 days).</summary>
    private const int MaxManualPeriodDays = 31;

    /// <summary>
    /// Initializes a new instance of <see cref="ManualReportTriggerFunction"/>.
    /// </summary>
    /// <param name="reportFunction">
    /// The weekly report function whose <c>GenerateAndDispatchReportAsync</c> is reused.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="correlationIdAccessor">Provides the job-scoped correlation ID.</param>
    public ManualReportTriggerFunction(
        WeeklyComplianceReportFunction reportFunction,
        ILogger<ManualReportTriggerFunction> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _reportFunction = reportFunction ?? throw new ArgumentNullException(nameof(reportFunction));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
    }

    /// <summary>
    /// HTTP POST entry point for manual report re-run.
    /// </summary>
    /// <param name="request">
    /// The HTTP request containing a JSON body with <c>period_start</c> and
    /// <c>period_end</c> fields in ISO 8601 date format.
    /// </param>
    /// <param name="cancellationToken">Cancellation token injected by the runtime.</param>
    /// <returns>
    /// 202 Accepted when the report is generated and dispatched.
    /// 400 Bad Request when the request body is invalid.
    /// </returns>
    [Function(nameof(ManualReportTriggerFunction))]
    [Authorize(Policy = "ComplianceReporting")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/manual-run")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.CorrelationId;

        ManualRunRequest? body;
        try
        {
            body = await System.Text.Json.JsonSerializer.DeserializeAsync<ManualRunRequest>(
                request.Body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Manual report trigger rejected — invalid JSON body. CorrelationId={CorrelationId}",
                correlationId);

            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid request body",
                Detail = "Request body must be valid JSON with 'period_start' and 'period_end' ISO 8601 date fields.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (body is null)
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Missing request body",
                Detail = "Request body with 'period_start' and 'period_end' fields is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!DateTime.TryParse(body.PeriodStart, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTime periodStart))
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid period_start",
                Detail = $"'period_start' value '{body.PeriodStart}' is not a valid ISO 8601 date.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!DateTime.TryParse(body.PeriodEnd, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTime periodEnd))
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid period_end",
                Detail = $"'period_end' value '{body.PeriodEnd}' is not a valid ISO 8601 date.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        periodStart = DateTime.SpecifyKind(periodStart.Date, DateTimeKind.Utc);
        periodEnd = DateTime.SpecifyKind(periodEnd.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        if (periodStart > periodEnd)
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid date range",
                Detail = $"'period_start' ({body.PeriodStart}) must not be later than 'period_end' ({body.PeriodEnd}).",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if ((periodEnd - periodStart).TotalDays > MaxManualPeriodDays)
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Date range too large",
                Detail = $"Manual re-run is limited to {MaxManualPeriodDays} days. " +
                         "Use multiple requests for longer periods.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        _logger.LogInformation(
            "Manual compliance report re-run triggered. PeriodStart={PeriodStart} " +
            "PeriodEnd={PeriodEnd} CorrelationId={CorrelationId}",
            periodStart, periodEnd, correlationId);

        await _reportFunction.GenerateAndDispatchReportAsync(
            periodStart, periodEnd, cancellationToken, correlationId);

        return new AcceptedResult(
            location: (string?)null,
            value: new
            {
                message = "Compliance report generated and dispatched.",
                period_start = periodStart.ToString("yyyy-MM-dd"),
                period_end = periodEnd.ToString("yyyy-MM-dd"),
                correlation_id = correlationId,
            });
    }

    /// <summary>Deserialization target for the manual re-run request body.</summary>
    private sealed class ManualRunRequest
    {
        /// <summary>ISO 8601 date string for the start of the reporting period.</summary>
        public string? PeriodStart { get; init; }

        /// <summary>ISO 8601 date string for the end of the reporting period.</summary>
        public string? PeriodEnd { get; init; }
    }
}
