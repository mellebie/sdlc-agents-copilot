// TCPA Regulatory Compliance API
// Component: Reporting Controller
// Source: EPIC-005 (STORY-014, STORY-015) | SPEC-011, SPEC-012 | TASK-041, TASK-043
// Generated: 2026-06-26

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.Reporting;

namespace TCPA.Api.Controllers;

/// <summary>
/// Provides on-demand compliance report endpoints for Compliance Officers.
/// All endpoints require an authenticated caller with the <c>tcpa.compliance_officer</c>
/// or <c>tcpa.reporting</c> role (BR-051, BR-054, TASK-041, TASK-043).
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = "ComplianceReporting")]
[Produces("application/json")]
public sealed class ReportingController : ControllerBase
{
    /// <summary>Maximum date range allowed per query (90 days).</summary>
    private static readonly int MaxDateRangeDays = 90;

    private readonly IReportingService _reportingService;
    private readonly ILogger<ReportingController> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="ReportingController"/>.
    /// </summary>
    /// <param name="reportingService">Service providing compliance report data.</param>
    /// <param name="logger">Structured logger. Cell numbers are never emitted.</param>
    /// <param name="correlationIdAccessor">Provides the current request correlation ID.</param>
    public ReportingController(
        IReportingService reportingService,
        ILogger<ReportingController> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
    }

    /// <summary>
    /// Returns outbound SMS messages that were successfully forwarded to opted-in
    /// cell numbers within the specified date range (SPEC-011).
    /// </summary>
    /// <param name="from">
    /// Inclusive start of the date range. Required. ISO 8601 date (e.g., 2026-06-01).
    /// </param>
    /// <param name="to">
    /// Inclusive end of the date range. Required. Must be on or after <paramref name="from"/>.
    /// ISO 8601 date (e.g., 2026-06-07).
    /// </param>
    /// <param name="application">
    /// Optional. Filter results to a specific SCG application name (e.g., "GCMA").
    /// When omitted, records from all applications are returned.
    /// </param>
    /// <param name="cellNumber">
    /// Optional. Filter results to a specific cell number in E.164 format (e.g., +14045551234).
    /// When omitted, records for all cell numbers are returned.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// 200 OK with <c>{ records: [...], total_count: n }</c> on success.
    /// 400 Bad Request when parameters are missing, invalid, or the date range exceeds 90 days.
    /// 403 Forbidden when the caller lacks the required role.
    /// </returns>
    [HttpGet("opted-in")]
    [ProducesResponseType(typeof(ReportQueryResult<ForwardedSmsRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOptedInReport(
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "application")] string? application,
        [FromQuery(Name = "cell_number")] string? cellNumber,
        CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.CorrelationId;

        if (!TryParseAndValidateDateRange(from, to, out DateTime parsedFrom, out DateTime parsedTo,
            out string? validationError))
        {
            _logger.LogWarning(
                "Opted-in report rejected due to invalid date range. From={From} To={To} " +
                "Error={Error} CorrelationId={CorrelationId}",
                from, to, validationError, correlationId);

            return BadRequest(new ProblemDetails
            {
                Title = "Invalid date range",
                Detail = validationError,
                Status = StatusCodes.Status400BadRequest,
                Instance = correlationId,
            });
        }

        _logger.LogInformation(
            "Opted-in report requested. From={From} To={To} Application={Application} " +
            "CorrelationId={CorrelationId}",
            parsedFrom, parsedTo, application ?? "(all)", correlationId);

        ReportQueryFilter filter = new()
        {
            From = parsedFrom,
            To = parsedTo.Date.AddDays(1).AddTicks(-1), // include full end day
            ApplicationName = application,
            CellPhoneNumber = cellNumber,
        };

        try
        {
            ReportQueryResult<ForwardedSmsRecord> result =
                await _reportingService.QueryOptedInAsync(filter, cancellationToken);

            _logger.LogInformation(
                "Opted-in report returned {Count} records. CorrelationId={CorrelationId}",
                result.TotalCount, correlationId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid query parameters",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = correlationId,
            });
        }
    }

    /// <summary>
    /// Returns outbound SMS messages that were suppressed because the destination cell
    /// number had OPT_OUT status at send time (SPEC-012).
    /// </summary>
    /// <param name="from">
    /// Inclusive start of the date range. Required. ISO 8601 date.
    /// </param>
    /// <param name="to">
    /// Inclusive end of the date range. Required. Must be on or after <paramref name="from"/>.
    /// </param>
    /// <param name="application">
    /// Optional. Filter by SCG application name.
    /// </param>
    /// <param name="cellNumber">
    /// Optional. Filter by specific cell number in E.164 format.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// 200 OK with <c>{ records: [...], total_count: n }</c> on success.
    /// 400 Bad Request when parameters are invalid.
    /// 403 Forbidden when the caller lacks the required role.
    /// </returns>
    [HttpGet("opted-out")]
    [ProducesResponseType(typeof(ReportQueryResult<BlockedSmsRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOptedOutReport(
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "application")] string? application,
        [FromQuery(Name = "cell_number")] string? cellNumber,
        CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.CorrelationId;

        if (!TryParseAndValidateDateRange(from, to, out DateTime parsedFrom, out DateTime parsedTo,
            out string? validationError))
        {
            _logger.LogWarning(
                "Opted-out report rejected due to invalid date range. From={From} To={To} " +
                "Error={Error} CorrelationId={CorrelationId}",
                from, to, validationError, correlationId);

            return BadRequest(new ProblemDetails
            {
                Title = "Invalid date range",
                Detail = validationError,
                Status = StatusCodes.Status400BadRequest,
                Instance = correlationId,
            });
        }

        _logger.LogInformation(
            "Opted-out report requested. From={From} To={To} Application={Application} " +
            "CorrelationId={CorrelationId}",
            parsedFrom, parsedTo, application ?? "(all)", correlationId);

        ReportQueryFilter filter = new()
        {
            From = parsedFrom,
            To = parsedTo.Date.AddDays(1).AddTicks(-1), // include full end day
            ApplicationName = application,
            CellPhoneNumber = cellNumber,
        };

        try
        {
            ReportQueryResult<BlockedSmsRecord> result =
                await _reportingService.QueryOptedOutAsync(filter, cancellationToken);

            _logger.LogInformation(
                "Opted-out report returned {Count} records. CorrelationId={CorrelationId}",
                result.TotalCount, correlationId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid query parameters",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = correlationId,
            });
        }
    }

    /// <summary>
    /// Parses and validates the <paramref name="from"/> and <paramref name="to"/> query
    /// parameters as ISO 8601 dates. Sets <paramref name="validationError"/> when
    /// invalid and returns false.
    /// </summary>
    private bool TryParseAndValidateDateRange(
        string? from,
        string? to,
        out DateTime parsedFrom,
        out DateTime parsedTo,
        out string? validationError)
    {
        parsedFrom = default;
        parsedTo = default;
        validationError = null;

        if (string.IsNullOrWhiteSpace(from))
        {
            validationError = "Query parameter 'from' is required and must be an ISO 8601 date (e.g., 2026-06-01).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            validationError = "Query parameter 'to' is required and must be an ISO 8601 date (e.g., 2026-06-07).";
            return false;
        }

        if (!DateTime.TryParse(from, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out parsedFrom))
        {
            validationError = $"Query parameter 'from' value '{from}' is not a valid ISO 8601 date.";
            return false;
        }

        if (!DateTime.TryParse(to, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out parsedTo))
        {
            validationError = $"Query parameter 'to' value '{to}' is not a valid ISO 8601 date.";
            return false;
        }

        parsedFrom = DateTime.SpecifyKind(parsedFrom.Date, DateTimeKind.Utc);
        parsedTo = DateTime.SpecifyKind(parsedTo.Date, DateTimeKind.Utc);

        if (parsedFrom > parsedTo)
        {
            validationError = $"'from' ({from}) must not be later than 'to' ({to}).";
            return false;
        }

        if ((parsedTo - parsedFrom).TotalDays > MaxDateRangeDays)
        {
            validationError =
                $"The date range of {(int)(parsedTo - parsedFrom).TotalDays} days " +
                $"exceeds the maximum of {MaxDateRangeDays} days. Narrow the range and re-query.";
            return false;
        }

        return true;
    }
}
