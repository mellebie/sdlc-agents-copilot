// TCPA Regulatory Compliance API
// Component: Audit Log Write Exception
// Source: EPIC-004 | SPEC-008, SPEC-009, SPEC-010 | BR-042, BR-048, NFS-008
// Generated: 2026-06-26

namespace TCPA.Api.Services.AuditLog;

/// <summary>
/// Thrown when the audit log service cannot persist an entry to the database.
/// This exception signals a critical compliance failure — callers must never
/// swallow it. The event that triggered the audit write (e.g., opt-out status
/// update, message block) may or may not have been rolled back; callers are
/// responsible for documenting that behaviour in their own exception handling.
/// </summary>
public sealed class AuditLogWriteException : Exception
{
    /// <summary>
    /// Gets the event type of the audit entry that failed to persist.
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// Gets the correlation ID associated with the request during which the
    /// failure occurred. Used for operational alert correlation.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLogWriteException"/>.
    /// </summary>
    /// <param name="eventType">
    /// The event type string of the entry that could not be written.
    /// </param>
    /// <param name="correlationId">
    /// The correlation ID of the originating request.
    /// </param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="innerException">The underlying database exception.</param>
    public AuditLogWriteException(
        string eventType,
        string correlationId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
    }
}
