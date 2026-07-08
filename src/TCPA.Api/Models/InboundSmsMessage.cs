using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TCPA.Api.Models;

/// <summary>
/// Represents the inbound SMS webhook payload received from Cool Text.
/// All fields are required per the Cool Text webhook contract (SPEC-002).
/// </summary>
public sealed class InboundSmsMessage
{
    /// <summary>
    /// The Cool Text account identifier embedded in the inbound webhook payload.
    /// Used to resolve the originating SCG application registration.
    /// </summary>
    [Required]
    [JsonPropertyName("cool_text_account_id")]
    public string CoolTextAccountId { get; init; } = string.Empty;

    /// <summary>
    /// E.164-formatted cell phone number of the customer who sent the inbound SMS reply.
    /// This is PII — must be logged as last 4 digits only.
    /// </summary>
    [Required]
    [JsonPropertyName("sender_cell_number")]
    public string SenderCellNumber { get; init; } = string.Empty;

    /// <summary>
    /// Raw message body of the inbound SMS. Inspected for TCPA opt-out keywords (SPEC-003).
    /// Forwarded unchanged to the originating application if no opt-out keyword is detected.
    /// </summary>
    [Required]
    [JsonPropertyName("message_body")]
    public string MessageBody { get; init; } = string.Empty;

    /// <summary>
    /// Platform-assigned message identifier from Cool Text. Used for correlation and deduplication logging.
    /// </summary>
    [Required]
    [JsonPropertyName("cool_text_message_id")]
    public string CoolTextMessageId { get; init; } = string.Empty;
}
