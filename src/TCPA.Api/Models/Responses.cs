namespace TCPA.Api.Models;

public record InboundWebhookResponse(string Status, string InternalId);

public record OutboundMessageResponse(string Status, string? MessageId, DateTimeOffset? QueuedAt, string? SuppressionReason);

public record ReOptInResponse(long ReOptInId, string PhoneNumber, string Status, DateTimeOffset EffectiveAt);

public record HealthResponse(string Status, HealthChecks Checks, DateTimeOffset Timestamp);

public record HealthChecks(string Database, string Kafka);
