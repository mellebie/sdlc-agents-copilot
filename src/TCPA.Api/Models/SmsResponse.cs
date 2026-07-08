using System.Text.Json.Serialization;

namespace TCPA.Api.Models;

/// <summary>
/// Response returned to the upstream application after the outbound SMS compliance gate decision.
/// Status indicates whether the message was forwarded, suppressed, or passed through as unregistered.
/// </summary>
public sealed class SmsResponse
{
    /// <summary>
    /// Compliance gate outcome.
    /// FORWARDED: message passed the opt-out check and was forwarded to Cool Text.
    /// SUPPRESSED: message was blocked because the destination number has OPT_OUT status.
    /// UNREGISTERED_ACCOUNT: the Cool Text account ID is not in the Application Registry; message passed through without enforcement.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Cool Text message identifier returned when the message was forwarded.
    /// Null when the message was suppressed or unregistered.
    /// </summary>
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    /// <summary>
    /// Reason the message was suppressed. "OPT_OUT" when the destination number has opted out.
    /// Null when the message was forwarded or the account is unregistered.
    /// </summary>
    [JsonPropertyName("suppression_reason")]
    public string? SuppressionReason { get; init; }

    /// <summary>Creates a FORWARDED response with the Cool Text message identifier.</summary>
    public static SmsResponse Forwarded(string messageId) => new()
    {
        Status = SmsStatus.Forwarded,
        MessageId = messageId
    };

    /// <summary>Creates a SUPPRESSED response indicating the number has opted out of SMS.</summary>
    public static SmsResponse Suppressed() => new()
    {
        Status = SmsStatus.Suppressed,
        SuppressionReason = "OPT_OUT"
    };

    /// <summary>Creates an UNREGISTERED_ACCOUNT response for Cool Text account IDs not in the Application Registry.</summary>
    public static SmsResponse UnregisteredAccount() => new()
    {
        Status = SmsStatus.UnregisteredAccount
    };
}

/// <summary>Well-known status string constants for <see cref="SmsResponse"/>.</summary>
public static class SmsStatus
{
    /// <summary>Message was forwarded to Cool Text after passing the opt-out check.</summary>
    public const string Forwarded = "FORWARDED";

    /// <summary>Message was blocked because the destination number has OPT_OUT status.</summary>
    public const string Suppressed = "SUPPRESSED";

    /// <summary>Cool Text account ID is not registered; message passed through without enforcement.</summary>
    public const string UnregisteredAccount = "UNREGISTERED_ACCOUNT";
}

/// <summary>
/// Standard error response body returned for 4xx and 5xx status codes.
/// </summary>
public sealed class SmsErrorResponse
{
    /// <summary>Machine-readable error code identifying the error category.</summary>
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    /// <summary>Human-readable error message. Never contains PII, credentials, or internal stack details.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// For VALIDATION_ERROR responses: the list of field names that failed validation.
    /// Null for non-validation errors.
    /// </summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>Creates a 503 Service Unavailable error response for fail-closed compliance gate behavior.</summary>
    public static SmsErrorResponse ServiceUnavailable() => new()
    {
        Error = "SERVICE_UNAVAILABLE",
        Message = "Compliance check unavailable; message not forwarded."
    };

    /// <summary>Creates a 502 Bad Gateway error response when Cool Text is unreachable after the opt-in check passed.</summary>
    public static SmsErrorResponse BadGateway() => new()
    {
        Error = "BAD_GATEWAY",
        Message = "Downstream SMS platform unreachable."
    };
}

/// <summary>
/// Acknowledgement response returned to Cool Text after receiving an inbound webhook.
/// A 200 OK with this body is expected by Cool Text to prevent retry.
/// </summary>
public sealed class InboundAcknowledgement
{
    /// <summary>Always true — acknowledges receipt of the inbound webhook payload.</summary>
    [JsonPropertyName("received")]
    public bool Received { get; init; } = true;

    /// <summary>The singleton acknowledgement instance.</summary>
    public static readonly InboundAcknowledgement Instance = new();
}
