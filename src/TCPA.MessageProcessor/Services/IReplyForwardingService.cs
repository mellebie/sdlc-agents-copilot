using TCPA.MessageProcessor.Messaging;

namespace TCPA.MessageProcessor.Services;

public interface IReplyForwardingService
{
    /// <summary>
    /// Forwards the inbound message body to the application callback URL via HTTP POST.
    /// Best-effort — never throws. Non-2xx and network errors are logged and swallowed.
    /// BR-015: Body is forwarded byte-for-byte identical.
    /// BR-017: No retry.
    /// </summary>
    Task ForwardReplyAsync(InboundMessageEvent @event, string callbackUrl, CancellationToken ct);
}
