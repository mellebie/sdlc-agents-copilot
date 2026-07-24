namespace TCPA.MessageProcessor.Messaging;

/// <summary>
/// Kafka message produced by TCPA.Api when an inbound SMS webhook is received.
/// Deserialized from the inbound-messages topic payload.
/// </summary>
public record InboundMessageEvent(
    string InternalId,
    string MessageId,
    string From,
    string To,
    string Body,
    string Provider,
    string CoolTextAccountNumber,
    string ApplicationId,
    DateTimeOffset Timestamp);
