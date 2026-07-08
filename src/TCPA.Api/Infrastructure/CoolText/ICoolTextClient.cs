using TCPA.Api.Models;

namespace TCPA.Api.Infrastructure.CoolText;

/// <summary>
/// Result of a successful outbound SMS send via the Cool Text platform.
/// </summary>
public sealed class SendSmsResult
{
    /// <summary>The Cool Text platform message identifier.</summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>Delivery status reported by Cool Text (e.g., "queued", "sent").</summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Sends outbound SMS messages via the Cool Text / Twilio platform.
/// Consumed by <see cref="Services.OptOut.ConfirmationDispatcher"/> (opt-out confirmation SMS)
/// and <see cref="Services.SmsProxy.OutboundSmsGate"/> (application-originated messages).
/// </summary>
public interface ICoolTextClient
{
    /// <summary>
    /// Sends an outbound SMS from <paramref name="fromAccountId"/> to <paramref name="toPhoneNumber"/>.
    /// Used by <see cref="Services.OptOut.ConfirmationDispatcher"/> for confirmation messages.
    /// </summary>
    /// <param name="fromAccountId">Cool Text account number to send from.</param>
    /// <param name="toPhoneNumber">Destination E.164 cell phone number (PII — log last 4 only).</param>
    /// <param name="messageBody">SMS body text.</param>
    /// <returns>The Cool Text platform message identifier string.</returns>
    /// <exception cref="CoolTextApiException">Thrown on HTTP error or unexpected API response.</exception>
    Task<string> SendSmsAsync(string fromAccountId, string toPhoneNumber, string messageBody);

    /// <summary>
    /// Sends an outbound SMS with explicit cancellation support.
    /// Used by <see cref="Services.SmsProxy.OutboundSmsGate"/> for compliance-gated messages.
    /// Returns the full <see cref="SendSmsResult"/> including delivery status.
    /// </summary>
    Task<SendSmsResult> SendSmsAsync(
        string fromAccountId,
        string toPhoneNumber,
        string messageBody,
        CancellationToken cancellationToken);
}


/// <summary>
/// Extends the base Cool Text SMS client with the ability to forward inbound SMS webhook
/// messages to registered SCG application callback URLs.
///
/// <para>
/// This interface is consumed by <see cref="Services.SmsProxy.InboundSmsHandler"/> for
/// inbound message routing (SPEC-002). The outbound SMS sending capability is defined in
/// <see cref="ICoolTextClient"/> and implemented by the same concrete class.
/// </para>
/// </summary>
public interface ICoolTextForwardingClient
{
    /// <summary>
    /// Forwards an inbound SMS message to the registered SCG application's callback URL.
    /// Implements retry with exponential backoff (up to 3 attempts: 1s, 2s, 4s delays) per SPEC-002.
    /// Logs a permanent delivery failure if all retries are exhausted.
    /// </summary>
    /// <param name="applicationWebhookUrl">
    /// The HTTPS callback URL of the SCG application registered for this Cool Text account.
    /// </param>
    /// <param name="message">
    /// The original inbound SMS message payload received from Cool Text.
    /// </param>
    /// <exception cref="CoolTextForwardingException">
    /// Thrown when all retry attempts to the application callback URL have been exhausted.
    /// </exception>
    Task ForwardToApplicationAsync(string applicationWebhookUrl, InboundSmsMessage message);
}

/// <summary>
/// Thrown when all retry attempts to forward an inbound message to the SCG application callback URL
/// have been exhausted (SPEC-002 — max 3 attempts with exponential backoff).
/// </summary>
public sealed class CoolTextForwardingException : Exception
{
    /// <summary>The callback URL that could not be reached after all retries.</summary>
    public string CallbackUrl { get; }

    /// <summary>Number of delivery attempts made before giving up.</summary>
    public int AttemptCount { get; }

    /// <summary>Initializes a new instance describing the forwarding failure.</summary>
    public CoolTextForwardingException(string callbackUrl, int attemptCount, Exception? innerException = null)
        : base($"Failed to forward inbound SMS to {callbackUrl} after {attemptCount} attempt(s).", innerException)
    {
        CallbackUrl = callbackUrl;
        AttemptCount = attemptCount;
    }
}

/// <summary>
/// Thrown when the Cool Text SMS API returns an error or is unreachable.
/// The caller (OutboundSmsGate) rethrows this so the controller returns 502 Bad Gateway.
/// </summary>
public sealed class CoolTextApiException : Exception
{
    /// <summary>HTTP status code returned by Cool Text, if the error was an HTTP error response.</summary>
    public int? StatusCode { get; }

    /// <summary>Initializes a new instance without an HTTP status code (e.g., network error).</summary>
    public CoolTextApiException(string message, Exception? innerException = null)
        : base(message, innerException) { }

    /// <summary>Initializes a new instance with the HTTP status code from the Cool Text response.</summary>
    public CoolTextApiException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
