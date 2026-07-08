// src/TCPA.Api/Services/ReOptIn/ReOptInService.cs
// TCPA Compliance Engine — Re-Opt-In Service Implementation
// Source: TASK-028, TASK-029, TASK-038 | SPEC-007, SPEC-010 | STORY-009, STORY-010, STORY-013
// Business Rules: BR-031 through BR-038, BR-049, BR-050

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.AuditLog;

namespace TCPA.Api.Services.ReOptIn;

/// <summary>
/// Implements the privileged admin re-opt-in workflow: status lookup and
/// manual OPT-IN status write with full audit trail (SPEC-007, SPEC-010).
/// </summary>
/// <remarks>
/// All cell phone number values are PII; only the last four digits are
/// written to any log output (BR-068 / NFS-007c).
/// </remarks>
public sealed class ReOptInService : IReOptInService
{
    /// <summary>
    /// Sentinel status value returned when no record exists for a cell
    /// number, so the controller can distinguish "no record" from "OPT_IN".
    /// </summary>
    public const string NoRecordStatus = "NO_RECORD";

    private readonly TcpaDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ReOptInService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ReOptInService"/>.
    /// </summary>
    /// <param name="dbContext">EF Core database context for the TCPA store.</param>
    /// <param name="auditLogService">Service for writing immutable audit log entries.</param>
    /// <param name="logger">Structured logger.</param>
    public ReOptInService(
        TcpaDbContext dbContext,
        IAuditLogService auditLogService,
        ILogger<ReOptInService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<OptOutStatusResult?> GetStatusAsync(
        string cellPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cellPhoneNumber))
            throw new ArgumentException("Cell phone number must not be null or whitespace.", nameof(cellPhoneNumber));

        string maskedNumber = MaskPhoneNumber(cellPhoneNumber);

        _logger.LogInformation(
            "Admin status lookup for number ****{Masked}.",
            maskedNumber);

        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellPhoneNumber, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            _logger.LogInformation(
                "No opt-out record found for number ****{Masked}; returning null (HTTP 404).",
                maskedNumber);
            return null;
        }

        return new OptOutStatusResult
        {
            MaskedCellNumber = "******" + cellPhoneNumber[^4..],
            OptOutStatus = record.Status == OptOutStatus.OptOut ? "OPT_OUT" : "OPT_IN",
            LastOptOutTimestamp = record.LastOptOutTimestamp,
            LastOptInTimestamp = record.LastOptInTimestamp,
        };
    }

    /// <inheritdoc/>
    public async Task<ReOptInResult> ReOptInAsync(
        string cellPhoneNumber,
        string requestedBy,
        string reason,
        string? ticketReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cellPhoneNumber))
            throw new ArgumentException("Cell phone number must not be null or whitespace.", nameof(cellPhoneNumber));
        if (string.IsNullOrWhiteSpace(requestedBy))
            throw new ArgumentException("requestedBy (agent user ID) must not be null or whitespace.", nameof(requestedBy));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 20)
            throw new ArgumentException("Reason must be at least 20 characters.", nameof(reason));

        string maskedNumber = MaskPhoneNumber(cellPhoneNumber);
        DateTime eventTimestamp = DateTime.UtcNow;

        _logger.LogInformation(
            "Admin re-opt-in initiated for number ****{Masked} by agent {AgentId}.",
            maskedNumber, requestedBy);

        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellPhoneNumber, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            // No record at all — re-opt-in endpoint is only for reversing a prior
            // opt-out.  Numbers that never opted out are already OPT-IN by default.
            // Return a sentinel so the controller can respond with HTTP 409 (BR-038).
            _logger.LogWarning(
                "Re-opt-in rejected for number ****{Masked}: no prior opt-out record exists. " +
                "Agent: {AgentId}.",
                maskedNumber, requestedBy);

            return new ReOptInResult
            {
                Success = false,
                PreviousStatus = NoRecordStatus,
                NewStatus = NoRecordStatus,
                UpdatedTimestamp = eventTimestamp,
                RecordId = null,
                Message = "No opt-out record exists for this number. " +
                          "Re-opt-in is only valid after a prior opt-out.",
            };
        }

        string previousStatus = record.Status == OptOutStatus.OptOut ? "OPT_OUT" : "OPT_IN";

        if (record.Status == OptOutStatus.OptIn)
        {
            // Idempotent — number is already OPT-IN; log the action but take
            // no further status-change action (BR-035).
            _logger.LogInformation(
                "Re-opt-in for number ****{Masked} is a no-op: already OPT-IN. " +
                "Logging action for audit trail. Agent: {AgentId}.",
                maskedNumber, requestedBy);

            await WriteAuditLogAsync(
                record, requestedBy, reason, ticketReference,
                previousStatus, eventTimestamp, cancellationToken)
                .ConfigureAwait(false);

            return new ReOptInResult
            {
                Success = true,
                PreviousStatus = "OPT_IN",
                NewStatus = "OPT_IN",
                UpdatedTimestamp = eventTimestamp,
                RecordId = record.Id,
                Message = "Number was already OPT-IN. No status change made; action logged.",
            };
        }

        // Record exists and is OPT-OUT — update to OPT-IN.
        record.Status = OptOutStatus.OptIn;
        record.LastOptInTimestamp = eventTimestamp;
        record.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Number ****{Masked} successfully re-opted-in by agent {AgentId} (record {RecordId}).",
            maskedNumber, requestedBy, record.Id);

        await WriteAuditLogAsync(
            record, requestedBy, reason, ticketReference,
            previousStatus, eventTimestamp, cancellationToken)
            .ConfigureAwait(false);

        return new ReOptInResult
        {
            Success = true,
            PreviousStatus = "OPT_OUT",
            NewStatus = "OPT_IN",
            UpdatedTimestamp = eventTimestamp,
            RecordId = record.Id,
            Message = "Number successfully re-opted-in.",
        };
    }

    /// <summary>
    /// Writes the immutable RE_OPT_IN audit log entry.  A failure here is
    /// logged as a critical error but does NOT roll back the status change
    /// (NFS-008 principle applied to re-opt-in).
    /// </summary>
    private async Task WriteAuditLogAsync(
        CellNumberOptOutRecord record,
        string agentUserId,
        string reason,
        string? ticketReference,
        string previousStatus,
        DateTime eventTimestamp,
        CancellationToken cancellationToken)
    {
        var entry = new AuditLogEntry
        {
            EventType = AuditEventType.ReOptIn,
            EventTimestamp = eventTimestamp,
            CellPhoneNumber = record.CellPhoneNumber,
            OriginatingCoolTextAccountId = string.Empty,   // Not applicable for admin re-opt-in
            OriginatingApplicationName = "AdminAPI",
            SystemResponse = previousStatus == "OPT_OUT"
                ? "OPT_IN_STATUS_RESTORED"
                : "ALREADY_OPT_IN_ACTION_LOGGED",
            AgentUserId = agentUserId,
            Reason = reason,
            TicketReference = ticketReference,
            PreviousStatus = previousStatus == "OPT_OUT" ? OptOutStatus.OptOut : OptOutStatus.OptIn,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _auditLogService.LogAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string maskedNumber = MaskPhoneNumber(record.CellPhoneNumber);

            _logger.LogCritical(
                ex,
                "CRITICAL: Audit log write failed for RE_OPT_IN event on number ****{Masked} " +
                "by agent {AgentId}. The status change was persisted but the audit entry is missing. " +
                "Operations team must be alerted.",
                maskedNumber, agentUserId);

            // Do not re-throw — the status change is already committed (NFS-008 principle).
        }
    }

    /// <summary>
    /// Returns the last four digits of a phone number prefixed with asterisks
    /// (BR-068 / NFS-007c).
    /// </summary>
    private static string MaskPhoneNumber(string phoneNumber)
    {
        return phoneNumber.Length >= 4
            ? "****" + phoneNumber[^4..]
            : "****";
    }
}
