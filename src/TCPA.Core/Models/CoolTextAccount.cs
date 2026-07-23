namespace TCPA.Core.Models;

public class CoolTextAccount
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;  // Cool Text account number (1:1 with Gas app)
    public string ApplicationId { get; set; } = string.Empty;  // e.g. "BizTalk", "GCMA"
    public string ApplicationName { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;    // HTTPS URL for forwarding general replies
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
