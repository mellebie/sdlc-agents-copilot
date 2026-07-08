// src/TCPA.Api/Services/OptOut/IConfirmationDispatcher.cs
// TCPA Compliance Engine — Opt-Out Confirmation SMS Dispatcher Interface
// Source: TASK-021 | SPEC-005 | STORY-006
// Business Rules: BR-021 through BR-026

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Result returned by <see cref="IConfirmationDispatcher.DispatchAsync"/>.
/// </summary>
public sealed record ConfirmationDispatchResult
{
    /// <summary>
    /// <c>true</c> when the Cool Text API accepted the confirmation SMS.
    /// </summary>
    public bool ConfirmationSent { get; init; }

    /// <summary>
    /// The platform-assigned message identifier returned by Cool Text, or
    /// <c>null</c> when the dispatch failed.
    /// </summary>
    public string? CoolTextMessageId { get; init; }

    /// <summary>
    /// UTC timestamp at which the confirmation was dispatched, or
    /// <c>null</c> on failure.
    /// </summary>
    public DateTime? SendTimestamp { get; init; }

    /// <summary>
    /// Elapsed seconds from <c>inboundReceiptTimestamp</c> to the dispatch
    /// attempt.  Values greater than 60 indicate an SLA breach (NFS-001).
    /// </summary>
    public int SlaElapsedSeconds { get; init; }
}

/// <summary>
/// Dispatches the standardized opt-out confirmation SMS to the opted-out
/// customer via Cool Text/Twilio within the 60-second TCPA SLA (NFS-001).
/// </summary>
/// <remarks>
/// The confirmation is sent only once per opt-out event.  Re-triggering
/// opt-out on an already-opted-out number must NOT call this dispatcher
/// (BR-015 / BR-023).  A delivery failure does NOT reverse the opt-out
/// status (BR-025).
/// </remarks>
public interface IConfirmationDispatcher
{
    /// <summary>
    /// Sends the opt-out confirmation SMS to <paramref name="cellPhoneNumber"/>.
    /// </summary>
    /// <param name="cellPhoneNumber">
    /// E.164 number of the opted-out customer (PII — never log raw value).
    /// </param>
    /// <param name="coolTextAccountId">
    /// The Cool Text account that received the opt-out keyword.  The
    /// confirmation must be sent FROM this same account so the customer's
    /// device associates the reply with the same sender (BR-024).
    /// </param>
    /// <param name="inboundReceiptTimestamp">
    /// Timestamp of the inbound opt-out message receipt.  The SLA clock
    /// starts here, not at the time of this method call (BR-026).
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation requests.</param>
    /// <returns>A <see cref="ConfirmationDispatchResult"/> describing the outcome.</returns>
    Task<ConfirmationDispatchResult> DispatchAsync(
        string cellPhoneNumber,
        string coolTextAccountId,
        DateTime inboundReceiptTimestamp,
        CancellationToken cancellationToken = default);
}
