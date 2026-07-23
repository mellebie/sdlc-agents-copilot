namespace TCPA.Core.Models;

public class AuditLog
{
    public long Id { get; set; }
    public AuditEventType EventType { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? ApplicationId { get; set; }
    public string? MessageId { get; set; }
    public string? AgentId { get; set; }
    public string? Details { get; set; }  // JSON payload
    public bool AnomalyFlag { get; set; }
}
