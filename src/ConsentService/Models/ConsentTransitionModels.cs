namespace ConsentService.Models;

public enum ConsentStatus
{
    Unknown = 0,
    OptIn = 1,
    OptOut = 2
}

public enum TransitionState
{
    Completed = 0,
    Pending = 1,
    Failed = 2
}

public readonly record struct ConsentTransitionRequest(
    string EventId,
    string CustomerPhoneNumber,
    DateTimeOffset StopDetectedAtUtc,
    DateTimeOffset RequestedAtUtc);

public readonly record struct ConsentTransitionRecord(
    string TransitionId,
    string EventId,
    string CustomerPhoneNumber,
    ConsentStatus FromStatus,
    ConsentStatus ToStatus,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CompletionDeadlineUtc,
    TransitionState State,
    string StatusReason);

public readonly record struct ConsentTransitionResult(
    bool Success,
    bool IsIdempotent,
    string Code,
    ConsentTransitionRecord TransitionRecord);

public readonly record struct DeadlineRiskResult(bool AtRisk, string Reason);
