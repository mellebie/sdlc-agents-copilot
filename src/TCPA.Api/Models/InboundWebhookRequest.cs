using System.ComponentModel.DataAnnotations;

namespace TCPA.Api.Models;

public class InboundWebhookRequest
{
    [Required]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "From must be E.164 format (e.g. +14045551234).")]
    public string From { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "To must be E.164 format.")]
    public string To { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string Body { get; set; } = string.Empty;

    [Required]
    public string Provider { get; set; } = string.Empty;

    [Required]
    public string MessageId { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset Timestamp { get; set; }
}
