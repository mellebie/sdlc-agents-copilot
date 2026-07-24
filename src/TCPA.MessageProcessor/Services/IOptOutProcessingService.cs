using TCPA.MessageProcessor.Messaging;

namespace TCPA.MessageProcessor.Services;

/// <summary>
/// Result of an atomic opt-out processing operation.
/// </summary>
/// <param name="IsNew">True when the opt-out was newly written; false when the number was already opted out (duplicate).</param>
/// <param name="AuditRecordId">The generated audit log entry ID. Used by downstream services (e.g. ConfirmationDispatchService) to link their own audit entries.</param>
public record OptOutResult(bool IsNew, long AuditRecordId);

public interface IOptOutProcessingService
{
    /// <summary>
    /// Atomically writes the opt-out status and a corresponding audit log entry.
    /// Identifies duplicates (already opted-out) and writes OptOutDuplicate audit instead.
    /// BR-009: Opt-out status written before confirmation is triggered.
    /// BR-010: Audit log written atomically with opt-out record — both commit or both roll back.
    /// </summary>
    Task<OptOutResult> ProcessOptOutAsync(InboundMessageEvent @event, CancellationToken ct);
}
