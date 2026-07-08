// TCPA Regulatory Compliance API
// Component: Compliance Report Models
// Source: EPIC-005 (STORY-014, STORY-015, STORY-016) | SPEC-011, SPEC-012, SPEC-013
// Generated: 2026-06-26

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// A single record from the opted-in (forwarded SMS) report dataset.
/// Corresponds to SPEC-011 output fields.
/// </summary>
public sealed record ForwardedSmsRecord
{
    /// <summary>Status is always "FORWARDED" for this dataset.</summary>
    public string Status { get; init; } = "FORWARDED";

    /// <summary>
    /// The destination cell phone number in E.164 format. PII — encrypted at rest.
    /// Never emit this value in operational logs.
    /// </summary>
    public string CellPhoneNumber { get; init; } = string.Empty;

    /// <summary>Human-readable SCG application name (e.g., "GCMA", "KMI Active").</summary>
    public string OriginatingApplicationName { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the message was forwarded.</summary>
    public DateTime MessageTimestamp { get; init; }

    /// <summary>
    /// SMS message body. PII-adjacent; present in this dataset to support regulatory
    /// discovery requests (BR-052, SPEC-011).
    /// </summary>
    public string? MessageBody { get; init; }

    /// <summary>Cool Text account identifier that submitted the outbound request.</summary>
    public string CoolTextAccountId { get; init; } = string.Empty;
}

/// <summary>
/// A single record from the opted-out (blocked SMS) report dataset.
/// Corresponds to SPEC-012 output fields.
/// </summary>
public sealed record BlockedSmsRecord
{
    /// <summary>Status is always "BLOCKED" for this dataset.</summary>
    public string Status { get; init; } = "BLOCKED";

    /// <summary>
    /// The blocked destination cell phone number in E.164 format. PII — encrypted at rest.
    /// Never emit this value in operational logs.
    /// </summary>
    public string CellPhoneNumber { get; init; } = string.Empty;

    /// <summary>Human-readable SCG application name.</summary>
    public string OriginatingApplicationName { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the block decision was made.</summary>
    public DateTime AttemptTimestamp { get; init; }

    /// <summary>SMS message body of the suppressed message. Stored for regulatory discovery.</summary>
    public string? MessageBody { get; init; }

    /// <summary>Reason the message was suppressed. Value is always "OPT_OUT" (BR-054).</summary>
    public string SuppressionReason { get; init; } = "OPT_OUT";
}

/// <summary>
/// Aggregated data model for the automated weekly compliance report (SPEC-013).
/// Contains summary statistics and per-application breakdowns for the reporting period.
/// </summary>
public sealed class WeeklyComplianceReportData
{
    /// <summary>Inclusive start of the reporting period (Monday 00:00:00 UTC).</summary>
    public DateTime PeriodStart { get; init; }

    /// <summary>Inclusive end of the reporting period (Sunday 23:59:59 UTC).</summary>
    public DateTime PeriodEnd { get; init; }

    /// <summary>Total count of outbound SMS successfully forwarded to opted-in numbers.</summary>
    public int TotalForwardedCount { get; init; }

    /// <summary>Total count of outbound SMS suppressed due to OPT_OUT status.</summary>
    public int TotalBlockedCount { get; init; }

    /// <summary>Total count of opt-out events (new and duplicate) in the period.</summary>
    public int TotalOptOutEventCount { get; init; }

    /// <summary>Total count of manual re-opt-in actions performed in the period.</summary>
    public int TotalReOptInCount { get; init; }

    /// <summary>
    /// Opt-out enforcement success rate as a percentage.
    /// Calculated as: (TotalBlockedCount / (TotalBlockedCount + ComplianceFailureCount)) * 100.
    /// 100.0 when no compliance failures occurred; lower values indicate missed blocks.
    /// </summary>
    public double OptOutEnforcementSuccessRate { get; init; }

    /// <summary>Per-application breakdown of forwarded and blocked message counts.</summary>
    public IReadOnlyList<ApplicationBreakdown> ApplicationBreakdowns { get; init; }
        = Array.Empty<ApplicationBreakdown>();

    /// <summary>
    /// List of compliance failures detected in the period. A compliance failure is defined
    /// as an outbound SMS that was forwarded to a number that had OPT_OUT status at send time.
    /// An empty list indicates full compliance. Any entries here are regulatory risk items
    /// requiring immediate investigation (TASK-045, SPEC-013 AC-003).
    /// </summary>
    public IReadOnlyList<ComplianceFailure> ComplianceFailures { get; init; }
        = Array.Empty<ComplianceFailure>();

    /// <summary>
    /// True when the reporting database projection is stale (last update > 30 minutes ago).
    /// A stale projection means the report may not reflect the most recent activity.
    /// The report is still generated — the staleness is noted in the report body (TASK-045).
    /// </summary>
    public bool IsProjectionStale { get; init; }

    /// <summary>UTC timestamp when the reporting projection was last refreshed.</summary>
    public DateTime? ProjectionLastRefreshedAt { get; init; }
}

/// <summary>
/// Per-application message count breakdown within a reporting period.
/// </summary>
public sealed record ApplicationBreakdown
{
    /// <summary>Human-readable SCG application name.</summary>
    public string ApplicationName { get; init; } = string.Empty;

    /// <summary>Count of SMS forwarded to opted-in numbers from this application.</summary>
    public int ForwardedCount { get; init; }

    /// <summary>Count of SMS blocked due to OPT_OUT from this application.</summary>
    public int BlockedCount { get; init; }

    /// <summary>Count of opt-out events from this application's inbound messages.</summary>
    public int OptOutEventCount { get; init; }
}

/// <summary>
/// Represents a detected compliance failure: a message forwarded to a number
/// that held OPT_OUT status at send time. These are critical findings requiring
/// immediate investigation (SPEC-013 AC-003).
/// </summary>
public sealed record ComplianceFailure
{
    /// <summary>UTC timestamp of the forwarded message that should have been blocked.</summary>
    public DateTime MessageTimestamp { get; init; }

    /// <summary>
    /// Masked cell phone number (last 4 digits shown, e.g., "******1234").
    /// Full number available in the encrypted audit log for regulatory discovery.
    /// </summary>
    public string MaskedCellPhoneNumber { get; init; } = string.Empty;

    /// <summary>Application name that submitted the message.</summary>
    public string ApplicationName { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the OPT_OUT status was on record for this number.</summary>
    public DateTime OptOutStatusTimestamp { get; init; }
}

/// <summary>
/// Query filter for on-demand compliance reports (SPEC-011, SPEC-012).
/// </summary>
public sealed class ReportQueryFilter
{
    /// <summary>Inclusive start of the query date range (UTC). Required.</summary>
    public required DateTime From { get; init; }

    /// <summary>Inclusive end of the query date range (UTC). Required. Must be >= From.</summary>
    public required DateTime To { get; init; }

    /// <summary>
    /// Optional application name filter. When set, only records from this application
    /// are returned. When null, all applications are included.
    /// </summary>
    public string? ApplicationName { get; init; }

    /// <summary>
    /// Optional cell phone number filter. When set, only records for this specific
    /// cell number are returned. The value must be in E.164 format.
    /// When null, all cell numbers are included.
    /// </summary>
    public string? CellPhoneNumber { get; init; }
}

/// <summary>
/// Paginated response wrapper for on-demand report queries.
/// </summary>
/// <typeparam name="T">The report record type.</typeparam>
public sealed class ReportQueryResult<T>
{
    /// <summary>The matching records for the query. Empty list when no results match.</summary>
    public IReadOnlyList<T> Records { get; init; } = Array.Empty<T>();

    /// <summary>Total count of matching records. Equals Records.Count in Phase 1 (no pagination).</summary>
    public int TotalCount { get; init; }
}
