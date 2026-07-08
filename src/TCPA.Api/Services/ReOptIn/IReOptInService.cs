// src/TCPA.Api/Services/ReOptIn/IReOptInService.cs
// TCPA Compliance Engine — Re-Opt-In Service Interface
// Source: TASK-028, TASK-029 | SPEC-007 | STORY-009, STORY-010
// Business Rules: BR-031 through BR-038

namespace TCPA.Api.Services.ReOptIn;

/// <summary>
/// Outcome of a re-opt-in status lookup via
/// <see cref="IReOptInService.GetStatusAsync"/>.
/// </summary>
public sealed record OptOutStatusResult
{
    /// <summary>
    /// The last four digits of the cell number for display (PII masking).
    /// Never the full number (BR-037).
    /// </summary>
    public string MaskedCellNumber { get; init; } = string.Empty;

    /// <summary>The current opt-out status: "OPT_IN" or "OPT_OUT".</summary>
    public string OptOutStatus { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the most recent opt-out event, or <c>null</c> if
    /// the number has never opted out.
    /// </summary>
    public DateTime? LastOptOutTimestamp { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent re-opt-in action, or <c>null</c>
    /// if the number has never been re-opted-in.
    /// </summary>
    public DateTime? LastOptInTimestamp { get; init; }
}

/// <summary>
/// Outcome of a re-opt-in write via
/// <see cref="IReOptInService.ReOptInAsync"/>.
/// </summary>
public sealed record ReOptInResult
{
    /// <summary><c>true</c> when the status was successfully set to OPT-IN.</summary>
    public bool Success { get; init; }

    /// <summary>The status before this call: "OPT_IN", "OPT_OUT", or the sentinel "NO_RECORD".</summary>
    public string PreviousStatus { get; init; } = string.Empty;

    /// <summary>The new status after this call — "OPT_IN" on success, or "NO_RECORD" when rejected.</summary>
    public string NewStatus { get; init; } = string.Empty;

    /// <summary>UTC timestamp of the status update.</summary>
    public DateTime UpdatedTimestamp { get; init; }

    /// <summary>The opt-out record ID associated with this action, or <c>null</c> when rejected.</summary>
    public Guid? RecordId { get; init; }

    /// <summary>
    /// Human-readable message summarising the outcome (e.g. a no-op note
    /// when the number was already OPT-IN).
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Provides the privileged read/write operations required by the admin
/// re-opt-in workflow.  Only authenticated users with the
/// <c>tcpa.helpdesk</c> or <c>tcpa.compliance_officer</c> role may invoke
/// the write path (BR-031).
/// </summary>
public interface IReOptInService
{
    /// <summary>
    /// Returns the current opt-out status for <paramref name="cellPhoneNumber"/>.
    /// The returned value masks the cell number to the last four digits (BR-037).
    /// </summary>
    /// <param name="cellPhoneNumber">E.164 cell phone number (PII).</param>
    /// <param name="cancellationToken">Propagates cancellation requests.</param>
    /// <returns>
    /// The current status record, or <c>null</c> when no record exists for
    /// the supplied number (the caller should map this to HTTP 404).
    /// </returns>
    Task<OptOutStatusResult?> GetStatusAsync(
        string cellPhoneNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually resets <paramref name="cellPhoneNumber"/> to OPT-IN status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the number has no prior opt-out record at all, the method returns
    /// a result with <see cref="ReOptInResult.PreviousStatus"/> =
    /// <see cref="ReOptInService.NoRecordStatus"/> so the controller can
    /// respond with HTTP 409 (BR-038 / SPEC-007 error: 409 Conflict).
    /// </para>
    /// <para>
    /// If the number is already OPT-IN, the method returns success
    /// idempotently and logs the action (BR-035 / STORY-012 AC-004).
    /// </para>
    /// <para>
    /// No confirmation SMS is sent to the customer in Phase 1 (BR-036).
    /// </para>
    /// </remarks>
    /// <param name="cellPhoneNumber">E.164 cell phone number (PII).</param>
    /// <param name="requestedBy">
    /// Authenticated agent user ID extracted from the JWT token claim (not
    /// the request body — prevents spoofing).
    /// </param>
    /// <param name="reason">
    /// Mandatory free-text reason provided by the Help Desk agent; minimum
    /// 20 characters (TASK-029 validation rule).
    /// </param>
    /// <param name="ticketReference">
    /// Optional Help Desk ticket reference number.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation requests.</param>
    /// <returns>A <see cref="ReOptInResult"/> describing the outcome.</returns>
    Task<ReOptInResult> ReOptInAsync(
        string cellPhoneNumber,
        string requestedBy,
        string reason,
        string? ticketReference,
        CancellationToken cancellationToken = default);
}
