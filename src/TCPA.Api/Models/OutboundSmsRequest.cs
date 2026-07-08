using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TCPA.Api.Models;

/// <summary>
/// Represents an outbound SMS request submitted by an upstream SCG application to the TCPA compliance gate.
/// All required fields must be present; the API returns 400 with field-level detail for any missing or invalid field.
/// </summary>
public sealed class OutboundSmsRequest
{
    /// <summary>
    /// Cool Text account identifier that identifies the originating SCG application.
    /// Resolved against the Application Registry to determine compliance enforcement scope.
    /// </summary>
    [Required]
    [JsonPropertyName("cool_text_account_id")]
    public string CoolTextAccountId { get; init; } = string.Empty;

    /// <summary>
    /// Destination cell phone number in E.164 format (e.g., +12025551234).
    /// Validated against the TCPA opt-out database before forwarding.
    /// This is PII — must be logged as last 4 digits only.
    /// </summary>
    [Required]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "destination_cell_number must be in E.164 format (e.g., +12025551234).")]
    [JsonPropertyName("destination_cell_number")]
    public string DestinationCellNumber { get; init; } = string.Empty;

    /// <summary>
    /// SMS message body content. Must be non-empty and no longer than 1600 characters
    /// per Twilio/Cool Text platform limits for concatenated SMS.
    /// </summary>
    [Required]
    [StringLength(1600, MinimumLength = 1, ErrorMessage = "message_body must be between 1 and 1600 characters.")]
    [JsonPropertyName("message_body")]
    public string MessageBody { get; init; } = string.Empty;

    /// <summary>
    /// Optional caller-supplied reference identifier for logging and traceability.
    /// Not used in compliance gate logic; passed through to the audit log.
    /// </summary>
    [JsonPropertyName("originating_application_reference")]
    public string? OriginatingApplicationReference { get; init; }
}
