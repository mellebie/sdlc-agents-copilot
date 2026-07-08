// TCPA Regulatory Compliance API
// Component: Audit Log Service Interface
// Source: EPIC-004 (STORY-013, STORY-014, STORY-015) | SPEC-008, SPEC-009, SPEC-010
// Generated: 2026-06-26

using TCPA.Api.Domain;

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// Defines the contract for the append-only audit log service.
/// The audit log is immutable: no update or delete operations are exposed.
/// Every compliance-relevant event must be persisted before the calling
/// operation completes. A write failure must be surfaced to the caller —
/// silent failures are not permitted (NFS-008, BR-042, BR-048).
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Appends a new audit log entry to the immutable audit store.
    /// The service assigns <see cref="AuditLogEntry.RecordId"/> and
    /// <see cref="AuditLogEntry.CreatedAt"/> before persistence.
    /// </summary>
    /// <param name="entry">
    /// The fully-constructed audit log entry. The caller is responsible for
    /// populating all required fields before calling this method.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <see cref="Guid"/> of the newly-written audit record
    /// (<see cref="AuditLogEntry.RecordId"/>).
    /// </returns>
    /// <exception cref="AuditLogWriteException">
    /// Thrown when the entry cannot be persisted to the database. The caller
    /// must never swallow this exception — audit log write failures are
    /// critical compliance events (BR-042, BR-048, NFS-008).
    /// </exception>
    Task<Guid> LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries audit log entries within a date range, optionally filtered by
    /// application name and event type. Used by the reporting projection job.
    /// </summary>
    /// <param name="from">Inclusive start of the query window (UTC).</param>
    /// <param name="to">Inclusive end of the query window (UTC).</param>
    /// <param name="applicationName">
    /// Optional filter by <see cref="AuditLogEntry.OriginatingApplicationName"/>.
    /// Null returns all applications.
    /// </param>
    /// <param name="eventType">
    /// Optional filter by <see cref="AuditEventType"/>. Null returns all event types.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A read-only list of matching audit log entries ordered by
    /// <see cref="AuditLogEntry.EventTimestamp"/> ascending.
    /// </returns>
    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        DateTime from,
        DateTime to,
        string? applicationName = null,
        AuditEventType? eventType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience method that constructs and appends an <see cref="AuditEventType.OptOut"/>
    /// audit entry from the raw opt-out event parameters.
    /// </summary>
    Task<Guid> WriteOptOutEventAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience method that constructs and appends an <see cref="AuditEventType.BlockedOutbound"/>
    /// audit entry from the raw suppression event parameters.
    /// </summary>
    Task<Guid> WriteBlockedOutboundEventAsync(
        string cellPhoneNumber,
        string coolTextAccountId,
        string applicationName,
        string? messageBody,
        DateTime eventTimestamp,
        CancellationToken cancellationToken = default);
}
