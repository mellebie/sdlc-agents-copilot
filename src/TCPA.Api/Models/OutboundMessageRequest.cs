using System.ComponentModel.DataAnnotations;

namespace TCPA.Api.Models;

public class OutboundMessageRequest
{
    [Required]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "ToNumber must be E.164 format.")]
    public string ToNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(160, ErrorMessage = "SMS body must not exceed 160 characters.")]
    [MinLength(1)]
    public string Body { get; set; } = string.Empty;

    [Required]
    public string CoolTextAccountNumber { get; set; } = string.Empty;

    [Required]
    public string ApplicationId { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }
}
