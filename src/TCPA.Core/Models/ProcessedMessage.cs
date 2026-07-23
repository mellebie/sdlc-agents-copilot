namespace TCPA.Core.Models;

/// <summary>
/// Idempotency store for inbound webhook and outbound submission requests.
/// Keyed on provider messageId or caller correlationId.
/// </summary>
public class ProcessedMessage
{
    /// <summary>
    /// Primary key — provider messageId or correlationId.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// System-generated unique identifier for internal reference.
    /// </summary>
    public Guid InternalId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Status of the processed message. Values: "received" | "queued" | "suppressed".
    /// </summary>
    public string ResponseStatus { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was processed.
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Endpoint that handled the message. Values: "webhook" | "outbound".
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
}
