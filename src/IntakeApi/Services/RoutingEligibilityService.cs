using IntakeApi.Contracts;

namespace IntakeApi.Services;

public interface IRoutingEligibilityService
{
    RoutingEligibilityResult Evaluate(InboundMessageRequest request);
}

public sealed class RoutingEligibilityService : IRoutingEligibilityService
{
    private readonly IScopeMappingResolver _scopeMappingResolver;

    public RoutingEligibilityService(IScopeMappingResolver scopeMappingResolver)
    {
        _scopeMappingResolver = scopeMappingResolver;
    }

    public RoutingEligibilityResult Evaluate(InboundMessageRequest request)
    {
        if (request.SourceLdc is null || request.SourceApplication is null || string.IsNullOrWhiteSpace(request.CoolTextAccountId))
        {
            return new RoutingEligibilityResult(false, "REJECTED_OUT_OF_SCOPE", "unknown");
        }

        var resolution = _scopeMappingResolver.Resolve(request.SourceLdc.Value, request.SourceApplication.Value, request.CoolTextAccountId);
        return new RoutingEligibilityResult(resolution.IsRouteable, resolution.ReasonCode, resolution.MappingVersion);
    }
}

public readonly record struct RoutingEligibilityResult(bool IsEligible, string ReasonCode, string MappingVersion);
