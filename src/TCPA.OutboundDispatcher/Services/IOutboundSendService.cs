using TCPA.OutboundDispatcher.Messaging;

namespace TCPA.OutboundDispatcher.Services;

public interface IOutboundSendService
{
    /// <summary>
    /// Sends the outbound SMS via ICoolTextApiClient with up to 3 retries (2 s → 4 s → 8 s).
    /// Writes <c>OutboundDelivered</c> audit entry on success.
    /// Writes <c>OutboundFailed</c> audit entry when all retries are exhausted.
    /// Never throws — all errors are logged and recorded; the caller commits the Kafka offset.
    /// </summary>
    Task SendAsync(OutboundMessageEvent @event, CancellationToken ct);
}
