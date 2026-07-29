namespace IntakeApi.Contracts;

public sealed class EnforcementDecisionRequest
{
    public string? OutboundRequestId { get; init; }
    public string? CustomerPhoneNumber { get; init; }
    public SourceApplication? SourceApplication { get; init; }
    public SourceLdc? SourceLdc { get; init; }
    public ConsentDecisionStatus? ApplicationReportedStatus { get; init; }
}

public sealed class EnforcementDecisionResponse
{
    public string EnforcementDecision { get; init; } = string.Empty;
    public string DecisionReason { get; init; } = string.Empty;
    public DateTimeOffset DecisionTimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}

public enum ConsentDecisionStatus
{
    Unknown = 0,
    OptIn = 1,
    OptOut = 2
}
