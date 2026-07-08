// TCPA Regulatory Compliance API — Azure Functions Scheduler
// Component: Weekly Compliance Report Timer Trigger
// Source: EPIC-005 (STORY-016) | SPEC-013 | TASK-047
// Generated: 2026-06-26

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.Reporting;

namespace TCPA.Scheduler;

/// <summary>
/// Azure Functions Timer Trigger that runs every Monday at 06:00 UTC and generates
/// the weekly TCPA compliance report for the prior Monday–Sunday period (SPEC-013,
/// TASK-047).
///
/// <para>
/// Schedule: cron expression <c>0 6 * * 1</c> (Monday 06:00 UTC, every week).
/// </para>
///
/// <para>
/// Period calculation: The report always covers the 7-day window ending at
/// Sunday 23:59:59 UTC immediately preceding the trigger time. Running the function
/// on Monday at 06:00 means:
/// <list type="bullet">
///   <item>PeriodEnd = prior Sunday 23:59:59 UTC</item>
///   <item>PeriodStart = the Monday before PeriodEnd at 00:00:00 UTC</item>
/// </list>
/// </para>
///
/// <para>
/// Manual re-run: An HTTP-triggered companion (<see cref="ManualReportTriggerFunction"/>)
/// allows authorized callers to regenerate a report for any arbitrary period.
/// This is idempotent — the same period produces consistent output on re-run (AC-005).
/// </para>
///
/// <para>
/// Failure handling: Any unhandled exception is logged at Critical level, which
/// triggers an Azure Monitor alert to IT operations (TASK-047 AC-004). The function
/// does not retry automatically — IT must investigate and use the manual re-run trigger.
/// </para>
/// </summary>
public sealed class WeeklyComplianceReportFunction
{
    private readonly IReportingService _reportingService;
    private readonly IReportEmailer _reportEmailer;
    private readonly ILogger<WeeklyComplianceReportFunction> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="WeeklyComplianceReportFunction"/>.
    /// </summary>
    /// <param name="reportingService">Generates the aggregated report data.</param>
    /// <param name="reportEmailer">Dispatches the report via SMTP.</param>
    /// <param name="logger">Structured logger. Cell numbers are never emitted.</param>
    /// <param name="correlationIdAccessor">Provides the job-scoped correlation ID.</param>
    /// <param name="timeProvider">Abstracted clock for testable period calculation.</param>
    public WeeklyComplianceReportFunction(
        IReportingService reportingService,
        IReportEmailer reportEmailer,
        ILogger<WeeklyComplianceReportFunction> logger,
        ICorrelationIdAccessor correlationIdAccessor,
        TimeProvider timeProvider)
    {
        _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
        _reportEmailer = reportEmailer ?? throw new ArgumentNullException(nameof(reportEmailer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Timer-triggered entry point. Fires every Monday at 06:00 UTC.
    /// </summary>
    /// <param name="timerInfo">Timer metadata provided by the Azure Functions runtime.</param>
    /// <param name="cancellationToken">Cancellation token injected by the runtime.</param>
    [Function(nameof(WeeklyComplianceReportFunction))]
    public async Task RunAsync(
        [TimerTrigger("0 6 * * 1")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.CorrelationId;

        (DateTime periodStart, DateTime periodEnd) = CalculatePriorWeekPeriod(_timeProvider.GetUtcNow().UtcDateTime);

        _logger.LogInformation(
            "Weekly compliance report job started. PeriodStart={PeriodStart} " +
            "PeriodEnd={PeriodEnd} IsPastDue={IsPastDue} CorrelationId={CorrelationId}",
            periodStart,
            periodEnd,
            timerInfo.IsPastDue,
            correlationId);

        await GenerateAndDispatchReportAsync(periodStart, periodEnd, cancellationToken, correlationId);
    }

    /// <summary>
    /// Generates the weekly compliance report data and dispatches it via email.
    /// Extracted to a shared method so the manual HTTP trigger reuses the same logic.
    /// </summary>
    internal async Task GenerateAndDispatchReportAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken,
        string correlationId)
    {
        try
        {
            WeeklyComplianceReportData reportData = await _reportingService.GenerateWeeklyReportAsync(
                periodStart,
                periodEnd,
                cancellationToken);

            await _reportEmailer.SendAsync(reportData, cancellationToken);

            _logger.LogInformation(
                "Weekly compliance report job completed successfully. PeriodStart={PeriodStart} " +
                "PeriodEnd={PeriodEnd} ForwardedCount={Forwarded} BlockedCount={Blocked} " +
                "ComplianceFailures={Failures} CorrelationId={CorrelationId}",
                periodStart,
                periodEnd,
                reportData.TotalForwardedCount,
                reportData.TotalBlockedCount,
                reportData.ComplianceFailures.Count,
                correlationId);
        }
        catch (Exception ex)
        {
            // Log at Critical so Azure Monitor alert fires immediately (TASK-047 AC-004).
            _logger.LogCritical(
                ex,
                "WEEKLY REPORT JOB FAILURE — compliance report not delivered. " +
                "PeriodStart={PeriodStart} PeriodEnd={PeriodEnd} CorrelationId={CorrelationId}. " +
                "IT operations must investigate immediately.",
                periodStart,
                periodEnd,
                correlationId);

            // Re-throw so the Azure Functions runtime records the function as failed.
            throw;
        }
    }

    /// <summary>
    /// Calculates the Monday-to-Sunday period for the week immediately preceding
    /// the given trigger timestamp (TASK-045, TASK-047).
    /// </summary>
    /// <param name="triggerUtc">The UTC time at which the timer fired (typically Monday 06:00).</param>
    /// <returns>
    /// A tuple of (periodStart, periodEnd) where:
    /// <list type="bullet">
    ///   <item>periodStart is the prior Monday at 00:00:00 UTC</item>
    ///   <item>periodEnd is the prior Sunday at 23:59:59 UTC</item>
    /// </list>
    /// </returns>
    internal static (DateTime PeriodStart, DateTime PeriodEnd) CalculatePriorWeekPeriod(DateTime triggerUtc)
    {
        // Find the most recent Monday before (or on) the trigger date, then go back 7 days
        // to find the start of the reporting week (the Monday prior).
        int daysFromMonday = ((int)triggerUtc.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateTime thisMondayMidnight = triggerUtc.Date.AddDays(-daysFromMonday);

        // Prior week: from the Monday before this week.
        DateTime priorMonday = thisMondayMidnight.AddDays(-7);
        DateTime priorSunday = thisMondayMidnight.AddSeconds(-1); // Sunday 23:59:59

        return (
            PeriodStart: DateTime.SpecifyKind(priorMonday, DateTimeKind.Utc),
            PeriodEnd: DateTime.SpecifyKind(priorSunday, DateTimeKind.Utc));
    }
}
