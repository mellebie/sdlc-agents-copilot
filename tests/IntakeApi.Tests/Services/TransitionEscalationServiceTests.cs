using ConsentService.Models;
using ConsentService.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class TransitionEscalationServiceTests
{
    [Fact]
    public async Task EvaluateAndEscalateAsync_WhenNearDeadline_PublishesEscalation()
    {
        var policy = new ConsentTransitionPolicy { EscalationThresholdHours = 24 };
        var publisher = Substitute.For<ITransitionEscalationPublisher>();
        var service = new TransitionEscalationService(policy, publisher);

        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var transition = new ConsentTransitionRecord(
            TransitionId: "tr-1",
            EventId: "evt-1",
            CustomerPhoneNumber: "+14045550103",
            FromStatus: ConsentStatus.OptIn,
            ToStatus: ConsentStatus.OptOut,
            RequestedAtUtc: now.AddHours(-2),
            CompletedAtUtc: null,
            CompletionDeadlineUtc: now.AddHours(12),
            State: TransitionState.Pending,
            StatusReason: "PENDING");

        var result = await service.EvaluateAndEscalateAsync(transition, now);

        result.AtRisk.Should().BeTrue();
        result.Reason.Should().Contain("DEADLINE_RISK_");
        await publisher.Received(1).PublishAsync("tr-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAndEscalateAsync_WhenCompleted_DoesNotEscalate()
    {
        var policy = new ConsentTransitionPolicy { EscalationThresholdHours = 24 };
        var publisher = Substitute.For<ITransitionEscalationPublisher>();
        var service = new TransitionEscalationService(policy, publisher);

        var now = DateTimeOffset.UtcNow;
        var transition = new ConsentTransitionRecord(
            TransitionId: "tr-2",
            EventId: "evt-2",
            CustomerPhoneNumber: "+14045550104",
            FromStatus: ConsentStatus.OptIn,
            ToStatus: ConsentStatus.OptOut,
            RequestedAtUtc: now.AddHours(-1),
            CompletedAtUtc: now,
            CompletionDeadlineUtc: now.AddDays(5),
            State: TransitionState.Completed,
            StatusReason: "DONE");

        var result = await service.EvaluateAndEscalateAsync(transition, now);

        result.AtRisk.Should().BeFalse();
        result.Reason.Should().Be("COMPLETED");
        await publisher.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
