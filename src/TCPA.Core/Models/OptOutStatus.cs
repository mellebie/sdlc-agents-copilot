namespace TCPA.Core.Models;

/// <summary>
/// Tracks the current opt-out status for a given phone number.
/// One record per phone number; status is either "opted-in" or "opted-out".
/// </summary>
public class OptOutStatus
{
    public long Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "opted-in";  // "opted-in" | "opted-out"
    public DateTime EffectiveAt { get; set; }
    public long AuditRecordId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
