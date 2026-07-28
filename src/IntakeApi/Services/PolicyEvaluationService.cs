using IntakeApi.Contracts;

namespace IntakeApi.Services;

public interface IConsentLookupService
{
    Task<ConsentLookupResult> GetConsentStatusAsync(string customerPhoneNumber, CancellationToken cancellationToken = default);
}

public interface IDivergenceAuditPublisher
{
    Task PublishAsync(string outboundRequestId, string reason, CancellationToken cancellationToken = default);
}

public sealed class NullDivergenceAuditPublisher : IDivergenceAuditPublisher
{
    public Task PublishAsync(string outboundRequestId, string reason, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public readonly record struct ConsentLookupResult(bool Success, ConsentDecisionStatus Status, string Code);
public readonly record struct PolicyEvaluationResult(bool Success, bool GuardedFailure, bool OutOfScope, string Decision, string Reason);

public interface IPolicyEvaluationService
{
    Task<PolicyEvaluationResult> EvaluateAsync(EnforcementDecisionRequest request, CancellationToken cancellationToken = default);
}

public sealed class InMemoryConsentLookupService : IConsentLookupService
{
    private static readonly Dictionary<string, ConsentDecisionStatus> Seed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["+14045550100"] = ConsentDecisionStatus.OptOut,
        ["+14045550101"] = ConsentDecisionStatus.OptIn,
        ["+14045550102"] = ConsentDecisionStatus.Unknown
    };

    public Task<ConsentLookupResult> GetConsentStatusAsync(string customerPhoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.Equals(customerPhoneNumber, "+14045550999", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ConsentLookupResult(false, ConsentDecisionStatus.Unknown, "CONSENT_LOOKUP_FAILED"));
        }

        if (Seed.TryGetValue(customerPhoneNumber, out var status))
        {
            return Task.FromResult(new ConsentLookupResult(true, status, "OK"));
        }

        return Task.FromResult(new ConsentLookupResult(true, ConsentDecisionStatus.Unknown, "OK"));
    }
}

public sealed class PolicyEvaluationService : IPolicyEvaluationService
{
    private readonly IConsentLookupService _consentLookupService;
    private readonly IDivergenceAuditPublisher _divergenceAuditPublisher;

    public PolicyEvaluationService(IConsentLookupService consentLookupService, IDivergenceAuditPublisher divergenceAuditPublisher)
    {
        _consentLookupService = consentLookupService;
        _divergenceAuditPublisher = divergenceAuditPublisher;
    }

    public async Task<PolicyEvaluationResult> EvaluateAsync(EnforcementDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SourceApplication is null || request.SourceApplication == SourceApplication.Unknown || request.SourceLdc is null || request.SourceLdc == SourceLdc.Unknown)
        {
            return new PolicyEvaluationResult(false, false, true, string.Empty, "OUT_OF_SCOPE");
        }

        var lookup = await _consentLookupService.GetConsentStatusAsync(request.CustomerPhoneNumber!, cancellationToken);
        if (!lookup.Success)
        {
            return new PolicyEvaluationResult(false, true, false, string.Empty, "CONSENT_LOOKUP_FAILED");
        }

        if (request.ApplicationReportedStatus == ConsentDecisionStatus.OptIn && lookup.Status == ConsentDecisionStatus.OptOut)
        {
            await _divergenceAuditPublisher.PublishAsync(request.OutboundRequestId!, "APP_STATUS_TAKES_PRECEDENCE", cancellationToken);
            return new PolicyEvaluationResult(true, false, false, "ALLOW", "APP_STATUS_TAKES_PRECEDENCE");
        }

        if (lookup.Status == ConsentDecisionStatus.OptOut)
        {
            return new PolicyEvaluationResult(true, false, false, "BLOCK", "API_OPTED_OUT");
        }

        return new PolicyEvaluationResult(true, false, false, "ALLOW", "UNKNOWN");
    }
}
