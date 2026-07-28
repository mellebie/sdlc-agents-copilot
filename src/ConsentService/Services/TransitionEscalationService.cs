using ConsentService.Models;

namespace ConsentService.Services;

public interface ITransitionEscalationPublisher
{
    Task PublishAsync(string transitionId, string reason, CancellationToken cancellationToken = default);
}

public sealed class NullTransitionEscalationPublisher : ITransitionEscalationPublisher
{
    public Task PublishAsync(string transitionId, string reason, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class ConsentTransitionPolicy
{
    public int CompletionWindowDays { get; init; } = 10;
    public int EscalationThresholdHours { get; init; } = 24;
    public int IdempotencyWindowHours { get; init; } = 24;
}

public interface ITransitionEscalationService
{
    Task<DeadlineRiskResult> EvaluateAndEscalateAsync(ConsentTransitionRecord transition, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

public sealed class TransitionEscalationService : ITransitionEscalationService
{
    private readonly ConsentTransitionPolicy _policy;
    private readonly ITransitionEscalationPublisher _publisher;

    public TransitionEscalationService(ConsentTransitionPolicy policy, ITransitionEscalationPublisher publisher)
    {
        _policy = policy;
        _publisher = publisher;
    }

    public async Task<DeadlineRiskResult> EvaluateAndEscalateAsync(ConsentTransitionRecord transition, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (transition.State == TransitionState.Completed)
        {
            return new DeadlineRiskResult(false, "COMPLETED");
        }

        var remaining = transition.CompletionDeadlineUtc - nowUtc;
        if (remaining <= TimeSpan.FromHours(_policy.EscalationThresholdHours))
        {
            var reason = $"DEADLINE_RISK_{Math.Max(0, (int)Math.Round(remaining.TotalHours))}H";
            await _publisher.PublishAsync(transition.TransitionId, reason, cancellationToken);
            return new DeadlineRiskResult(true, reason);
        }

        return new DeadlineRiskResult(false, "WITHIN_THRESHOLD");
    }
}
