using ConsentService.Models;
using ConsentService.Repositories;
using ConsentService.Security;

namespace ConsentService.Services;

public interface IReOptInSecurityEventPublisher
{
    Task PublishAsync(string requestId, string reasonCode, CancellationToken cancellationToken = default);
}

public sealed class NullReOptInSecurityEventPublisher : IReOptInSecurityEventPublisher
{
    public Task PublishAsync(string requestId, string reasonCode, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public interface IReOptInService
{
    Task<ReOptInTransitionResult> ProcessAsync(ReOptInTransitionRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReOptInService : IReOptInService
{
    private readonly IConsentStateRepository _stateRepository;
    private readonly IReOptInAuthorizationPolicy _authorizationPolicy;
    private readonly IReplayProtectionService _replayProtectionService;
    private readonly IReOptInSecurityEventPublisher _securityEventPublisher;

    public ReOptInService(
        IConsentStateRepository stateRepository,
        IReOptInAuthorizationPolicy authorizationPolicy,
        IReplayProtectionService replayProtectionService,
        IReOptInSecurityEventPublisher securityEventPublisher)
    {
        _stateRepository = stateRepository;
        _authorizationPolicy = authorizationPolicy;
        _replayProtectionService = replayProtectionService;
        _securityEventPublisher = securityEventPublisher;
    }

    public async Task<ReOptInTransitionResult> ProcessAsync(ReOptInTransitionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.InitiationChannel is not (ReOptInChannel.Form or ReOptInChannel.SmsResponse))
        {
            return new ReOptInTransitionResult(false, "INVALID_REOPTIN_CHANNEL", ConsentStatus.Unknown, "REJECTED", request.InitiatedAtUtc, false);
        }

        if (!_authorizationPolicy.IsAuthorized(request))
        {
            await _securityEventPublisher.PublishAsync(request.ReOptInRequestId, "REOPTIN_NOT_AUTHORIZED", cancellationToken);
            return new ReOptInTransitionResult(false, "REOPTIN_NOT_AUTHORIZED", ConsentStatus.Unknown, "REJECTED", request.InitiatedAtUtc, true);
        }

        if (_replayProtectionService.IsReplay(request.ReOptInRequestId, request.InitiatedAtUtc))
        {
            await _securityEventPublisher.PublishAsync(request.ReOptInRequestId, "REPLAY_DETECTED", cancellationToken);
            return new ReOptInTransitionResult(false, "REPLAY_DETECTED", ConsentStatus.Unknown, "REJECTED", request.InitiatedAtUtc, true);
        }

        _replayProtectionService.Remember(request.ReOptInRequestId, request.InitiatedAtUtc);

        var existing = await _stateRepository.GetStatusAsync(request.CustomerPhoneNumber, cancellationToken);
        if (existing == ConsentStatus.OptIn)
        {
            return new ReOptInTransitionResult(true, "NO_CHANGE", ConsentStatus.OptIn, "NO_CHANGE", request.InitiatedAtUtc, false);
        }

        await _stateRepository.SetStatusAsync(request.CustomerPhoneNumber, ConsentStatus.OptIn, cancellationToken);
        return new ReOptInTransitionResult(true, "UPDATED", ConsentStatus.OptIn, "UPDATED", request.InitiatedAtUtc, false);
    }
}
