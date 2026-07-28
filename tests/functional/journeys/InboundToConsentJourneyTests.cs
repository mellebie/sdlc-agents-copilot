using ConsentService.Models;
using ConsentService.Repositories;
using ConsentService.Services;
using FluentAssertions;
using IntakeApi.Contracts;
using IntakeApi.Controllers;
using IntakeApi.Services;
using IntentClassifier.Services;
using NSubstitute;
using Xunit;

namespace FunctionalTests.Journeys;

public sealed class InboundToConsentJourneyTests
{
    [Fact]
    public async Task StopMessage_EndToEndJourney_TransitionsToOptOut()
    {
        var intakeValidator = new InboundMessageRequestValidator();
        var routingService = Substitute.For<IRoutingEligibilityService>();
        routingService.Evaluate(Arg.Any<InboundMessageRequest>())
            .Returns(new RoutingEligibilityResult(true, "ROUTEABLE", "test"));
        var intakeController = new InboundMessagesController(new TestCorrelationIdGenerator(), routingService, intakeValidator);

        var intakeResult = intakeController.Post(new InboundMessageRequest
        {
            EventId = "evt-journey-1",
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "+14045550199",
            SourceLdc = SourceLdc.Vng,
            SourceApplication = SourceApplication.BizTalk,
            CoolTextAccountId = "acct-001",
            MessageText = "Stop"
        });

        intakeResult.Result.Should().NotBeNull();

        var classifier = new IntentClassificationService();
        var classification = classifier.Classify("Stop");
        classification.Intent.Should().Be(NormalizedIntent.Stop);

        var repo = new InMemoryConsentTransitionRepository();
        var escalation = Substitute.For<ITransitionEscalationService>();
        escalation.EvaluateAndEscalateAsync(Arg.Any<ConsentTransitionRecord>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DeadlineRiskResult(false, "OK"));
        var transitionService = new ConsentTransitionService(repo, new ConsentTransitionPolicy(), escalation);

        var transition = await transitionService.ProcessStopTransitionAsync(new ConsentTransitionRequest(
            "evt-journey-1", "+14045550199", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        transition.Success.Should().BeTrue();
        transition.TransitionRecord.ToStatus.Should().Be(ConsentStatus.OptOut);
    }

    private sealed class TestCorrelationIdGenerator : ICorrelationIdGenerator
    {
        public string Generate() => "corr-functional-1";
    }
}
