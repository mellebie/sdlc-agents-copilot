namespace TCPA.Api.Messaging;

/// <summary>
/// Abstraction for publishing TCPA message events to Kafka topics.
/// Controllers depend on this interface; <see cref="KafkaMessagePublisher"/> is the production implementation.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publishes an inbound SMS event to the inbound Kafka topic.</summary>
    Task PublishInboundAsync(InboundMessageEvent @event, CancellationToken ct);

    /// <summary>Publishes an outbound SMS event to the outbound Kafka topic.</summary>
    Task PublishOutboundAsync(OutboundMessageEvent @event, CancellationToken ct);

    /// <summary>
    /// Checks Kafka broker reachability. Returns <c>true</c> if metadata can be
    /// retrieved within the probe timeout; <c>false</c> on any error.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken ct);
}
