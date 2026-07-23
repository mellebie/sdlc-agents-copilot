namespace TCPA.Api.Messaging;

/// <summary>
/// Event published to the outbound Kafka topic when an SMS message is queued for delivery.
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
