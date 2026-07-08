// TCPA Regulatory Compliance API
// Component: Report Email Dispatch Exception
// Source: EPIC-005 (STORY-016) | TASK-046
// Generated: 2026-06-26

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// Thrown when the weekly compliance report email cannot be dispatched via SMTP.
/// Callers must not swallow this exception — an undelivered compliance report is
/// an operational failure requiring an alert to IT (TASK-046, TASK-047).
/// </summary>
public sealed class ReportEmailDispatchException : Exception
{
    /// <summary>Gets the reporting period start date associated with the failed dispatch.</summary>
    public DateTime PeriodStart { get; }

    /// <summary>Gets the reporting period end date associated with the failed dispatch.</summary>
    public DateTime PeriodEnd { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ReportEmailDispatchException"/>.
    /// </summary>
    /// <param name="periodStart">The start of the reporting period that failed to send.</param>
    /// <param name="periodEnd">The end of the reporting period that failed to send.</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="innerException">The underlying SMTP exception.</param>
    public ReportEmailDispatchException(
        DateTime periodStart,
        DateTime periodEnd,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }
}
