using ConsentService.Models;
using ConsentService.Repositories;

namespace ConsentService.Services;

public interface IConsentTransitionService
{
    Task<ConsentTransitionResult> ProcessStopTransitionAsync(ConsentTransitionRequest request, CancellationToken cancellationToken = default);
}

public interface ITransitionFailureAlertPublisher
{
    Task PublishAsync(string transitionId, string reasonCode, CancellationToken cancellationToken = default);
}

public sealed class NullTransitionFailureAlertPublisher : ITransitionFailureAlertPublisher
{
    public Task PublishAsync(string transitionId, string reasonCode, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class ConsentTransitionService : IConsentTransitionService
{
    private readonly IConsentTransitionRepository _repository;
    private readonly ConsentTransitionPolicy _policy;
    private readonly ITransitionEscalationService _escalationService;
    private readonly ITransitionFailureAlertPublisher _failureAlertPublisher;

    public ConsentTransitionService(
        IConsentTransitionRepository repository,
        ConsentTransitionPolicy policy,
        ITransitionEscalationService escalationService,
        ITransitionFailureAlertPublisher? failureAlertPublisher = null)
    {
        _repository = repository;
        _policy = policy;
        _escalationService = escalationService;
        _failureAlertPublisher = failureAlertPublisher ?? new NullTransitionFailureAlertPublisher();
    }

    public async Task<ConsentTransitionResult> ProcessStopTransitionAsync(ConsentTransitionRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = request.RequestedAtUtc;
        var idempotencyWindow = TimeSpan.FromHours(_policy.IdempotencyWindowHours);

        var existing = await _repository.FindByPhoneWithinWindowAsync(request.CustomerPhoneNumber, idempotencyWindow, nowUtc, cancellationToken);
        if (existing is not null && existing.Value.ToStatus == ConsentStatus.OptOut)
        {
            return new ConsentTransitionResult(true, true, "IDEMPOTENT_NO_CHANGE", existing.Value);
        }

        var deadline = request.StopDetectedAtUtc.AddDays(_policy.CompletionWindowDays);
        var transition = new ConsentTransitionRecord(
            TransitionId: Guid.NewGuid().ToString("N"),
            EventId: request.EventId,
            CustomerPhoneNumber: request.CustomerPhoneNumber,
            FromStatus: ConsentStatus.OptIn,
            ToStatus: ConsentStatus.OptOut,
            RequestedAtUtc: request.RequestedAtUtc,
            CompletedAtUtc: request.RequestedAtUtc,
            CompletionDeadlineUtc: deadline,
            State: TransitionState.Completed,
            StatusReason: "STOP_RECEIVED");

        try
        {
            await _repository.SaveAsync(transition, cancellationToken);
            await _escalationService.EvaluateAndEscalateAsync(transition, nowUtc, cancellationToken);
            return new ConsentTransitionResult(true, false, "UPDATED", transition);
        }
        catch (Exception)
        {
            var failed = transition with
            {
                CompletedAtUtc = null,
                State = TransitionState.Failed,
                StatusReason = "STATUS_STORE_UNAVAILABLE"
            };

            await _failureAlertPublisher.PublishAsync(failed.TransitionId, failed.StatusReason, cancellationToken);
            return new ConsentTransitionResult(false, false, "STATUS_STORE_UNAVAILABLE", failed);
        }
    }
}
