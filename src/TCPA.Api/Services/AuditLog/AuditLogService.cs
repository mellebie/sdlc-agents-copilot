// TCPA Regulatory Compliance API
// Component: Audit Log Service — Append-Only Implementation
// Source: EPIC-004 (STORY-013, STORY-014, STORY-015) | SPEC-008, SPEC-009, SPEC-010
// Generated: 2026-06-26

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// Append-only implementation of <see cref="IAuditLogService"/>.
/// This service enforces two non-negotiable invariants:
/// <list type="number">
///   <item>No UPDATE or DELETE operations are ever executed against the audit table.</item>
///   <item>A write failure throws <see cref="AuditLogWriteException"/> — it is never swallowed.</item>
/// </list>
/// Cell phone numbers are stored in their encrypted form as provided by the
/// domain model (Always Encrypted at the database column level). This service
/// never logs raw cell numbers — only the last 4 digits are emitted to the
/// operational log (NFS-007 / BR-068).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    /// <summary>5-year retention period applied to every new audit entry (NFS-004 / STORY-015).</summary>
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(365 * 5 + 2);

    private readonly TcpaDbContext _dbContext;
    private readonly ILogger<AuditLogService> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLogService"/>.
    /// </summary>
    /// <param name="dbContext">EF Core database context providing access to the audit log table.</param>
    /// <param name="logger">Structured logger. Cell numbers are masked before emission.</param>
    /// <param name="correlationIdAccessor">Provides the current request correlation ID.</param>
    public AuditLogService(
        TcpaDbContext dbContext,
        ILogger<AuditLogService> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method performs an INSERT only. Entity Framework's change tracker is
    /// configured to track the entry in Added state; SaveChangesAsync issues a single
    /// INSERT statement. The database-level DDL trigger (TASK-064) provides an additional
    /// enforcement layer rejecting any UPDATE or DELETE on the audit table.
    ///
    /// The <see cref="AuditLogEntry"/> uses <c>init</c>-only setters (immutable record-style).
    /// The caller must construct the entry with all required fields populated.
    /// </remarks>
    public async Task<Guid> LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string correlationId = _correlationIdAccessor.CorrelationId;

        _logger.LogInformation(
            "Writing audit log entry. EventType={EventType} ApplicationName={ApplicationName} " +
            "CellNumberSuffix={CellNumberSuffix} CorrelationId={CorrelationId} RecordId={RecordId}",
            entry.EventType,
            entry.OriginatingApplicationName,
            MaskCellNumber(entry.CellPhoneNumber),
            correlationId,
            entry.RecordId);

        try
        {
            // Add-only: never call Update or Remove on this entity.
            await _dbContext.AuditLogEntries.AddAsync(entry, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Audit log entry persisted. EventType={EventType} RecordId={RecordId} CorrelationId={CorrelationId}",
                entry.EventType,
                entry.RecordId,
                correlationId);

            return entry.RecordId;
        }
        catch (Exception ex) when (ex is not AuditLogWriteException)
        {
            // Log at Critical because a missed audit entry is a TCPA compliance failure (NFS-008).
            _logger.LogCritical(
                ex,
                "AUDIT LOG WRITE FAILURE — compliance event not persisted. " +
                "EventType={EventType} ApplicationName={ApplicationName} " +
                "CellNumberSuffix={CellNumberSuffix} CorrelationId={CorrelationId}. " +
                "Operations team must be alerted immediately.",
                entry.EventType,
                entry.OriginatingApplicationName,
                MaskCellNumber(entry.CellPhoneNumber),
                correlationId);

            throw new AuditLogWriteException(
                entry.EventType.ToString(),
                correlationId,
                $"Failed to write audit log entry for event type '{entry.EventType}'. " +
                "This is a critical compliance failure — the operations team must be alerted.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        DateTime from,
        DateTime to,
        string? applicationName = null,
        AuditEventType? eventType = null,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new ArgumentException(
                $"Query range is invalid: 'from' ({from:O}) must not be later than 'to' ({to:O}).",
                nameof(from));
        }

        IQueryable<AuditLogEntry> query = _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.EventTimestamp >= from && e.EventTimestamp <= to);

        if (applicationName is not null)
        {
            query = query.Where(e => e.OriginatingApplicationName == applicationName);
        }

        if (eventType is not null)
        {
            query = query.Where(e => e.EventType == eventType.Value);
        }

        List<AuditLogEntry> results = await query
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Audit log query completed. From={From} To={To} ApplicationName={ApplicationName} " +
            "EventType={EventType} ResultCount={ResultCount}",
            from,
            to,
            applicationName ?? "(all)",
            eventType?.ToString() ?? "(all)",
            results.Count);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public Task<Guid> WriteOptOutEventAsync(
        string cellPhoneNumber,
        string coolTextAccountId,
        string applicationName,
        string keyword,
        string? messageBody,
        string systemResponse,
        bool confirmationSent,
        DateTime? confirmationTimestamp,
        string confirmationStatus,
        DateTime eventTimestamp,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            RecordId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            EventType = AuditEventType.OptOut,
            CellPhoneNumber = cellPhoneNumber,
            OriginatingCoolTextAccountId = coolTextAccountId,
            OriginatingApplicationName = applicationName,
            OptOutKeywordReceived = keyword,
            MessageBody = messageBody,
            SystemResponse = systemResponse,
            ConfirmationSmsSentStatus = confirmationSent ? ConfirmationSmsStatus.Sent : ConfirmationSmsStatus.Failed,
            ConfirmationSmsTimestamp = confirmationTimestamp,
            EventTimestamp = eventTimestamp,
        };
        return LogAsync(entry, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Guid> WriteBlockedOutboundEventAsync(
        string cellPhoneNumber,
        string coolTextAccountId,
        string applicationName,
        string? messageBody,
        DateTime eventTimestamp,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            RecordId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            EventType = AuditEventType.BlockedOutbound,
            CellPhoneNumber = cellPhoneNumber,
            OriginatingCoolTextAccountId = coolTextAccountId,
            OriginatingApplicationName = applicationName,
            MessageBody = messageBody,
            SystemResponse = "MESSAGE_SUPPRESSED_OPT_OUT",
            SuppressionReason = "OPT_OUT",
            EventTimestamp = eventTimestamp,
        };
        return LogAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Computes the retention expiry date for an audit entry (EventTimestamp + 5 years).
    /// Used by callers that need to set <see cref="AuditLogEntry.RetentionExpiresAt"/>-equivalent
    /// metadata before construction (NFS-004 / STORY-015).
    /// </summary>
    /// <param name="eventTimestamp">The UTC event timestamp.</param>
    /// <returns>The UTC date after which the entry is eligible for purge.</returns>
    public static DateTime ComputeRetentionExpiry(DateTime eventTimestamp) =>
        eventTimestamp.Add(RetentionPeriod);

    /// <summary>
    /// Returns a masked representation of a cell number for operational log emission.
    /// Shows only the last 4 digits. Never emits the raw or full decrypted number (BR-068).
    /// If the value is shorter than 4 characters, returns "****".
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
