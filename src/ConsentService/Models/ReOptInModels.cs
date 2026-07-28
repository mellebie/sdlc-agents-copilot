namespace ConsentService.Models;

public enum ReOptInChannel
{
    Unknown = 0,
    Form = 1,
    SmsResponse = 2
}

public readonly record struct ReOptInTransitionRequest(
    string ReOptInRequestId,
    string CustomerPhoneNumber,
    ReOptInChannel InitiationChannel,
    DateTimeOffset InitiatedAtUtc,
    string? AuthorizationProof,
    string? ReplayNonce);

public readonly record struct ReOptInTransitionResult(
    bool Success,
    string Code,
    ConsentStatus UpdatedStatus,
    string UpdateResult,
    DateTimeOffset UpdateTimestampUtc,
    bool SecurityEventRaised);
