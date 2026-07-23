using System.ComponentModel.DataAnnotations;

namespace TCPA.Api.Models;

public class ReOptInRequest
{
    [Required]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "PhoneNumber must be E.164 format.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    [MinLength(1)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string AgentId { get; set; } = string.Empty;
}
