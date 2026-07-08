// TCPA Regulatory Compliance API
// Component: Reporting Service Interface
// Source: EPIC-005 (STORY-014, STORY-015, STORY-016) | SPEC-011, SPEC-012, SPEC-013
// Generated: 2026-06-26

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// Defines the contract for the compliance reporting service.
/// Provides on-demand query access to forwarded-SMS and blocked-SMS report datasets,
/// and generates the aggregated data model used by the weekly automated report.
/// </summary>
public interface IReportingService
{
    /// <summary>
    /// Queries the forwarded-SMS reporting projection for messages delivered to opted-in
    /// cell numbers (SPEC-011). Results are drawn from the projection database, not the
    /// live audit log.
    /// </summary>
    /// <param name="filter">
    /// Date range and optional application/cell number filters. Date range is validated
    /// before querying; a range exceeding 90 days is rejected.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ReportQueryResult{T}"/> containing matching forwarded-SMS records
    /// ordered by message timestamp ascending. Returns an empty result when no records
    /// match — never returns null.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the filter is invalid (e.g., date_to before date_from,
    /// range exceeding 90 days, invalid E.164 cell number format).
    /// </exception>
    Task<ReportQueryResult<ForwardedSmsRecord>> QueryOptedInAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the blocked-SMS reporting projection for messages suppressed due to OPT_OUT
    /// status (SPEC-012). Results are drawn from the projection database, not the live
    /// audit log.
    /// </summary>
    /// <param name="filter">
    /// Date range and optional application/cell number filters. Same validation rules
    /// as <see cref="QueryOptedInAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ReportQueryResult{T}"/> containing matching blocked-SMS records
    /// ordered by attempt timestamp ascending. Returns an empty result when no records
    /// match — never returns null.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the filter is invalid.
    /// </exception>
    Task<ReportQueryResult<BlockedSmsRecord>> QueryOptedOutAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the aggregated weekly compliance report data for a given period (SPEC-013).
    /// Used by the Azure Functions weekly report job and the manual re-run HTTP trigger.
    /// Always generates a report even when all counts are zero (TASK-045 AC-002).
    /// </summary>
    /// <param name="periodStart">
    /// Inclusive start of the reporting period (Monday 00:00:00 UTC).
    /// </param>
    /// <param name="periodEnd">
    /// Inclusive end of the reporting period (Sunday 23:59:59 UTC).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A fully-populated <see cref="WeeklyComplianceReportData"/> instance.
    /// The caller is responsible for dispatching the report via email.
    /// </returns>
    Task<WeeklyComplianceReportData> GenerateWeeklyReportAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken = default);
}
