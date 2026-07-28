using ConsentService.Models;
using ConsentService.Repositories;
using ConsentService.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class ConsentTransitionServiceTests
{
    [Fact]
    public async Task ProcessStopTransitionAsync_UpdatesToOptOutAndSetsDeadline()
    {
        var repository = new InMemoryConsentTransitionRepository();
        var escalation = Substitute.For<ITransitionEscalationService>();
        escalation.EvaluateAndEscalateAsync(Arg.Any<ConsentTransitionRecord>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DeadlineRiskResult(false, "COMPLETED"));
        var policy = new ConsentTransitionPolicy { CompletionWindowDays = 10, IdempotencyWindowHours = 24 };
        var service = new ConsentTransitionService(repository, policy, escalation);
        var requestTime = new DateTimeOffset(2026, 7, 28, 18, 0, 0, TimeSpan.Zero);

        var result = await service.ProcessStopTransitionAsync(new ConsentTransitionRequest("evt-1", "+14045550100", requestTime, requestTime));

        result.Success.Should().BeTrue();
        result.IsIdempotent.Should().BeFalse();
        result.TransitionRecord.ToStatus.Should().Be(ConsentStatus.OptOut);
        result.TransitionRecord.CompletionDeadlineUtc.Should().Be(requestTime.AddDays(10));
    }

    [Fact]
    public async Task ProcessStopTransitionAsync_RepeatedStop_IsIdempotent()
    {
        var repository = new InMemoryConsentTransitionRepository();
        var escalation = Substitute.For<ITransitionEscalationService>();
        escalation.EvaluateAndEscalateAsync(Arg.Any<ConsentTransitionRecord>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DeadlineRiskResult(false, "COMPLETED"));
        var service = new ConsentTransitionService(repository, new ConsentTransitionPolicy(), escalation);
        var now = DateTimeOffset.UtcNow;

        var first = await service.ProcessStopTransitionAsync(new ConsentTransitionRequest("evt-a", "+14045550101", now, now));
        var second = await service.ProcessStopTransitionAsync(new ConsentTransitionRequest("evt-b", "+14045550101", now.AddMinutes(5), now.AddMinutes(5)));

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.IsIdempotent.Should().BeTrue();
        second.Code.Should().Be("IDEMPOTENT_NO_CHANGE");
    }

    [Fact]
    public async Task ProcessStopTransitionAsync_WhenRepositoryFails_ReturnsFailedStateAndPublishesAlert()
    {
        var repository = Substitute.For<IConsentTransitionRepository>();
        repository.FindByPhoneWithinWindowAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ConsentTransitionRecord?)null);
        repository.SaveAsync(Arg.Any<ConsentTransitionRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("store unavailable")));

        var escalation = Substitute.For<ITransitionEscalationService>();
        var alertPublisher = Substitute.For<ITransitionFailureAlertPublisher>();
        var service = new ConsentTransitionService(repository, new ConsentTransitionPolicy(), escalation, alertPublisher);
        var now = DateTimeOffset.UtcNow;

        var result = await service.ProcessStopTransitionAsync(new ConsentTransitionRequest("evt-x", "+14045550102", now, now));

        result.Success.Should().BeFalse();
        result.Code.Should().Be("STATUS_STORE_UNAVAILABLE");
        result.TransitionRecord.State.Should().Be(TransitionState.Failed);
        await alertPublisher.Received(1).PublishAsync(result.TransitionRecord.TransitionId, "STATUS_STORE_UNAVAILABLE", Arg.Any<CancellationToken>());
    }
}
