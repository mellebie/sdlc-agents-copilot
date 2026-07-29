using ConsentService.Models;

namespace IntakeApi.Contracts;

public sealed class ReOptInRequest
{
    public string? ReOptInRequestId { get; init; }
    public string? CustomerPhoneNumber { get; init; }
    public ReOptInChannel? InitiationChannel { get; init; }
    public DateTimeOffset? InitiatedAtUtc { get; init; }
}

public sealed class ReOptInResponse
{
    public string UpdatedConsentStatus { get; init; } = string.Empty;
    public string UpdateResult { get; init; } = string.Empty;
    public DateTimeOffset UpdateTimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}
