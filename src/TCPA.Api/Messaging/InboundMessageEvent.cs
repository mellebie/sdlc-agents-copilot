namespace TCPA.Api.Messaging;

/// <summary>
/// Event published to the inbound Kafka topic when an SMS message is received from a customer.
/// Partition key: <see cref="From"/> (customer phone number).
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
