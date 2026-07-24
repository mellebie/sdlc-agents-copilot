namespace TCPA.OutboundDispatcher.Messaging;

/// <summary>
/// Kafka message consumed from the outbound-messages topic.
/// Published by TCPA.Api when an authorized application queues an SMS for delivery.
/// Partition key: <see cref="ToNumber"/> (destination phone number).
/// </summary>
public record OutboundMessageEvent(
    string MessageId,
    string ToNumber,
    string Body,
    string CoolTextAccountNumber,
    string ApplicationId,
    string? CorrelationId,
    DateTimeOffset QueuedAt);
