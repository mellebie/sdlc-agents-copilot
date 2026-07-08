using TCPA.Api.Models;

namespace TCPA.Api.Services.SmsProxy;

/// <summary>
/// Processes inbound SMS webhook messages received from Cool Text.
///
/// This handler is responsible for:
/// 1. Detecting TCPA opt-out keywords in the message body (delegating to IOptOutDetector).
/// 2. If an opt-out keyword is found: recording the opt-out, sending the confirmation SMS,
///    and forwarding the original message to the originating application (SPEC-002, SPEC-003).
/// 3. If no opt-out keyword is found: forwarding the message to the originating
///    application's registered callback URL only (SPEC-002).
///
/// The controller returns 200 OK to Cool Text before invoking this handler,
/// so this method runs after the HTTP response has already been sent.
/// </summary>
public interface IInboundSmsHandler
{
    /// <summary>
    /// Processes an inbound SMS message received from the Cool Text platform.
    /// </summary>
    /// <param name="message">The validated inbound SMS webhook payload.</param>
    /// <param name="cancellationToken">Cancellation token for the background processing task.</param>
    Task HandleAsync(InboundSmsMessage message, CancellationToken cancellationToken = default);
}
