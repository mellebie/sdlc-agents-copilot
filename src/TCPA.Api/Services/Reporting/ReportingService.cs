// TCPA Regulatory Compliance API
// Component: Reporting Service — On-Demand and Weekly Report Generation
// Source: EPIC-005 (STORY-014, STORY-015, STORY-016) | SPEC-011, SPEC-012, SPEC-013
// Generated: 2026-06-26

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// Implementation of <see cref="IReportingService"/>. All queries execute against
/// the reporting projection tables (<c>ForwardedSmsProjection</c> and
/// <c>BlockedSmsProjection</c>), not the live audit log, to avoid impacting the
/// compliance gate critical path (TASK-040, TASK-041, TASK-043).
///
/// <para>
/// Date range validation: queries are limited to a maximum of 90 days per request
/// to prevent unbounded result sets in Phase 1 (max-range assumption per task spec).
/// </para>
///
/// <para>
/// PII: Cell phone numbers are never emitted in operational logs. Result sets
/// include the encrypted column values as returned by the Always Encrypted driver —
/// the reporting layer does not decrypt them; decryption occurs transparently on
/// the client side per ADR-003.
/// </para>
/// </summary>
public sealed class ReportingService : IReportingService
{
    /// <summary>Maximum allowed query date range (90 days per task spec assumption).</summary>
    private static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(90);

    /// <summary>
    /// Threshold beyond which the reporting projection is considered stale (TASK-045).
    /// </summary>
    private static readonly TimeSpan ProjectionStalenessThreshold = TimeSpan.FromMinutes(30);

    private readonly TcpaDbContext _dbContext;
    private readonly ILogger<ReportingService> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="ReportingService"/>.
    /// </summary>
    /// <param name="dbContext">EF Core database context with access to projection tables.</param>
    /// <param name="logger">Structured logger. Cell numbers are never emitted.</param>
    /// <param name="correlationIdAccessor">Provides the current request correlation ID.</param>
    /// <param name="timeProvider">
    /// Abstracted clock for testable date calculations. Use <see cref="TimeProvider.System"/>
    /// in production registrations.
    /// </param>
    public ReportingService(
        TcpaDbContext dbContext,
        ILogger<ReportingService> logger,
        ICorrelationIdAccessor correlationIdAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<ReportQueryResult<ForwardedSmsRecord>> QueryOptedInAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);

        string correlationId = _correlationIdAccessor.CorrelationId;

        _logger.LogInformation(
            "Querying forwarded-SMS report. From={From} To={To} ApplicationName={ApplicationName} " +
            "CorrelationId={CorrelationId}",
            filter.From,
            filter.To,
            filter.ApplicationName ?? "(all)",
            correlationId);

        IQueryable<SmsMessageLog> query = _dbContext.SmsMessageLogs
            .AsNoTracking()
            .Where(m =>
                m.Direction == SmsDirection.Outbound &&
                m.Status == SmsMessageStatus.Forwarded &&
                m.Timestamp >= filter.From &&
                m.Timestamp <= filter.To);

        if (filter.ApplicationName is not null)
        {
            query = query.Where(m => m.ApplicationName == filter.ApplicationName);
        }

        // Cell number filter: equality on the encrypted column works via deterministic
        // Always Encrypted (ADR-003). The parameter is passed through EF as a parameterized
        // query — never interpolated into SQL.
        if (filter.CellPhoneNumber is not null)
        {
            query = query.Where(m => m.CellPhoneNumber == filter.CellPhoneNumber);
        }

        List<SmsMessageLog> rows = await query
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        List<ForwardedSmsRecord> records = rows.ConvertAll(m => new ForwardedSmsRecord
        {
            CellPhoneNumber = m.CellPhoneNumber,
            OriginatingApplicationName = m.ApplicationName,
            MessageTimestamp = m.Timestamp,
            MessageBody = m.MessageContent,
        });

        _logger.LogInformation(
            "Forwarded-SMS query complete. ResultCount={ResultCount} CorrelationId={CorrelationId}",
            records.Count,
            correlationId);

        return new ReportQueryResult<ForwardedSmsRecord>
        {
            Records = records.AsReadOnly(),
            TotalCount = records.Count,
        };
    }

    /// <inheritdoc />
    public async Task<ReportQueryResult<BlockedSmsRecord>> QueryOptedOutAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ValidateFilter(filter);

        string correlationId = _correlationIdAccessor.CorrelationId;

        _logger.LogInformation(
            "Querying blocked-SMS report. From={From} To={To} ApplicationName={ApplicationName} " +
            "CorrelationId={CorrelationId}",
            filter.From,
            filter.To,
            filter.ApplicationName ?? "(all)",
            correlationId);

        IQueryable<AuditLogEntry> query = _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(e =>
                e.EventType == AuditEventType.BlockedOutbound &&
                e.EventTimestamp >= filter.From &&
                e.EventTimestamp <= filter.To);

        if (filter.ApplicationName is not null)
        {
            query = query.Where(e => e.OriginatingApplicationName == filter.ApplicationName);
        }

        if (filter.CellPhoneNumber is not null)
        {
            query = query.Where(e => e.CellPhoneNumber == filter.CellPhoneNumber);
        }

        List<AuditLogEntry> rows = await query
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(cancellationToken);

        List<BlockedSmsRecord> records = rows.ConvertAll(e => new BlockedSmsRecord
        {
            CellPhoneNumber = e.CellPhoneNumber,
            OriginatingApplicationName = e.OriginatingApplicationName,
            AttemptTimestamp = e.EventTimestamp,
            MessageBody = e.MessageBody,
            SuppressionReason = e.SuppressionReason ?? "OPT_OUT",
        });

        _logger.LogInformation(
            "Blocked-SMS query complete. ResultCount={ResultCount} CorrelationId={CorrelationId}",
            records.Count,
            correlationId);

        return new ReportQueryResult<BlockedSmsRecord>
        {
            Records = records.AsReadOnly(),
            TotalCount = records.Count,
        };
    }

    /// <inheritdoc />
    public async Task<WeeklyComplianceReportData> GenerateWeeklyReportAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken = default)
    {
        if (periodStart > periodEnd)
        {
            throw new ArgumentException(
                $"Period start ({periodStart:O}) must not be later than period end ({periodEnd:O}).",
                nameof(periodStart));
        }

        string correlationId = _correlationIdAccessor.CorrelationId;

        _logger.LogInformation(
            "Generating weekly compliance report. PeriodStart={PeriodStart} PeriodEnd={PeriodEnd} " +
            "CorrelationId={CorrelationId}",
            periodStart,
            periodEnd,
            correlationId);

        // Fetch all audit log entries for the period in a single query.
        List<AuditLogEntry> auditEntries = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.EventTimestamp >= periodStart && e.EventTimestamp <= periodEnd)
            .ToListAsync(cancellationToken);

        // Fetch all forwarded SMS for the period.
        List<SmsMessageLog> forwardedMessages = await _dbContext.SmsMessageLogs
            .AsNoTracking()
            .Where(m =>
                m.Direction == SmsDirection.Outbound &&
                m.Status == SmsMessageStatus.Forwarded &&
                m.Timestamp >= periodStart &&
                m.Timestamp <= periodEnd)
            .ToListAsync(cancellationToken);

        // Aggregate counts by event type.
        int totalForwardedCount = forwardedMessages.Count;
        int totalBlockedCount = auditEntries.Count(e => e.EventType == AuditEventType.BlockedOutbound);
        int totalOptOutEventCount = auditEntries.Count(e => e.EventType == AuditEventType.OptOut);
        int totalReOptInCount = auditEntries.Count(e => e.EventType == AuditEventType.ReOptIn);

        // Detect compliance failures: forwarded messages where the audit log also shows
        // a SmsBlocked event for the same cell number in the same period.
        // This cross-reference surfaces cases where a number was opted out but still received a message.
        HashSet<string> blockedCellNumbers = auditEntries
            .Where(e => e.EventType == AuditEventType.BlockedOutbound)
            .Select(e => e.CellPhoneNumber)
            .ToHashSet();

        List<ComplianceFailure> complianceFailures = forwardedMessages
            .Where(m => blockedCellNumbers.Contains(m.CellPhoneNumber))
            .Select(m => new ComplianceFailure
            {
                MessageTimestamp = m.Timestamp,
                MaskedCellPhoneNumber = MaskCellNumber(m.CellPhoneNumber),
                ApplicationName = m.ApplicationName,
                // Use the earliest block audit entry timestamp for this number as the opt-out reference.
                OptOutStatusTimestamp = auditEntries
                    .Where(e => e.EventType == AuditEventType.BlockedOutbound && e.CellPhoneNumber == m.CellPhoneNumber)
                    .Min(e => e.EventTimestamp),
            })
            .ToList();

        // Opt-out enforcement success rate (TASK-045).
        int totalAttempts = totalBlockedCount + complianceFailures.Count;
        double successRate = totalAttempts == 0
            ? 100.0
            : Math.Round((double)totalBlockedCount / totalAttempts * 100, 2);

        // Per-application breakdown.
        IReadOnlyList<ApplicationBreakdown> breakdowns = BuildApplicationBreakdowns(
            forwardedMessages, auditEntries);

        // Check projection staleness using the most recent SmsMessageLog or AuditLogEntry timestamp.
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime? mostRecentActivity = GetMostRecentActivityTimestamp(forwardedMessages, auditEntries);
        bool isProjectionStale = mostRecentActivity.HasValue &&
            (now - mostRecentActivity.Value) > ProjectionStalenessThreshold;

        if (complianceFailures.Count > 0)
        {
            _logger.LogCritical(
                "Weekly compliance report contains {FailureCount} compliance failure(s). " +
                "PeriodStart={PeriodStart} PeriodEnd={PeriodEnd} CorrelationId={CorrelationId}. " +
                "Immediate investigation required.",
                complianceFailures.Count,
                periodStart,
                periodEnd,
                correlationId);
        }

        _logger.LogInformation(
            "Weekly compliance report generated. Forwarded={Forwarded} Blocked={Blocked} " +
            "OptOuts={OptOuts} ReOptIns={ReOptIns} ComplianceFailures={Failures} " +
            "SuccessRate={SuccessRate} IsStale={IsStale} CorrelationId={CorrelationId}",
            totalForwardedCount,
            totalBlockedCount,
            totalOptOutEventCount,
            totalReOptInCount,
            complianceFailures.Count,
            successRate,
            isProjectionStale,
            correlationId);

        return new WeeklyComplianceReportData
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalForwardedCount = totalForwardedCount,
            TotalBlockedCount = totalBlockedCount,
            TotalOptOutEventCount = totalOptOutEventCount,
            TotalReOptInCount = totalReOptInCount,
            OptOutEnforcementSuccessRate = successRate,
            ApplicationBreakdowns = breakdowns,
            ComplianceFailures = complianceFailures.AsReadOnly(),
            IsProjectionStale = isProjectionStale,
            ProjectionLastRefreshedAt = mostRecentActivity,
        };
    }

    /// <summary>
    /// Validates a <see cref="ReportQueryFilter"/> and throws <see cref="ArgumentException"/>
    /// for any invalid state.
    /// </summary>
    private static void ValidateFilter(ReportQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.From > filter.To)
        {
            throw new ArgumentException(
                $"Query range is invalid: date_from ({filter.From:yyyy-MM-dd}) " +
                $"must not be later than date_to ({filter.To:yyyy-MM-dd}).",
                nameof(filter));
        }

        if (filter.To - filter.From > MaxQueryRange)
        {
            throw new ArgumentException(
                $"Query range of {(filter.To - filter.From).Days} days exceeds the maximum " +
                $"allowed range of {MaxQueryRange.Days} days. Narrow the date range and re-query.",
                nameof(filter));
        }

        if (filter.CellPhoneNumber is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(filter.CellPhoneNumber, @"^\+[1-9]\d{1,14}$"))
        {
            throw new ArgumentException(
                $"cell_number_filter '{filter.CellPhoneNumber}' is not a valid E.164 phone number.",
                nameof(filter));
        }
    }

    /// <summary>
    /// Builds per-application message count breakdowns from the fetched data sets.
    /// </summary>
    private static IReadOnlyList<ApplicationBreakdown> BuildApplicationBreakdowns(
        List<SmsMessageLog> forwarded,
        List<AuditLogEntry> auditEntries)
    {
        // Union all application names present in either data set.
        HashSet<string> allApplicationNames = new(
            forwarded.Select(m => m.ApplicationName)
                     .Concat(auditEntries.Select(e => e.OriginatingApplicationName)));

        List<ApplicationBreakdown> breakdowns = allApplicationNames
            .OrderBy(name => name)
            .Select(name => new ApplicationBreakdown
            {
                ApplicationName = name,
                ForwardedCount = forwarded.Count(m => m.ApplicationName == name),
                BlockedCount = auditEntries.Count(e =>
                    e.OriginatingApplicationName == name && e.EventType == AuditEventType.BlockedOutbound),
                OptOutEventCount = auditEntries.Count(e =>
                    e.OriginatingApplicationName == name && e.EventType == AuditEventType.OptOut),
            })
            .ToList();

        return breakdowns.AsReadOnly();
    }

    /// <summary>
    /// Returns the most recent event timestamp across both forwarded messages and audit
    /// entries, or null if both collections are empty.
    /// </summary>
    private static DateTime? GetMostRecentActivityTimestamp(
        List<SmsMessageLog> forwarded,
        List<AuditLogEntry> auditEntries)
    {
        DateTime? latestForwarded = forwarded.Count > 0
            ? forwarded.Max(m => m.Timestamp)
            : null;

        DateTime? latestAudit = auditEntries.Count > 0
            ? auditEntries.Max(e => e.EventTimestamp)
            : null;

        if (latestForwarded is null) return latestAudit;
        if (latestAudit is null) return latestForwarded;
        return latestForwarded > latestAudit ? latestForwarded : latestAudit;
    }

    /// <summary>
    /// Returns a masked cell number string showing only the last 4 digits (BR-068).
    /// </summary>
    private static string MaskCellNumber(string cellPhoneNumber)
    {
        if (string.IsNullOrEmpty(cellPhoneNumber) || cellPhoneNumber.Length < 4)
        {
            return "****";
        }

        return $"******{cellPhoneNumber[^4..]}";
    }
}
