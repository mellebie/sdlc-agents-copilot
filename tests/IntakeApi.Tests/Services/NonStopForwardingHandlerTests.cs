using FluentAssertions;
using IntentClassifier.Handlers;
using IntentClassifier.Services;
using NSubstitute;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class NonStopForwardingHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithHelpIntent_ForwardsAndReturnsSuccess()
    {
        var callbackClient = Substitute.For<IApplicationCallbackClient>();
        callbackClient.ForwardAsync(Arg.Any<NonStopForwardingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationCallbackResult(true, "ok"));
        var outcomeRepository = Substitute.For<IForwardingOutcomeRepository>();
        var handler = new NonStopForwardingHandler(callbackClient, outcomeRepository);

        var request = CreateRequest(NormalizedIntent.Help, ConsentStatus.OptIn);
        var result = await handler.HandleAsync(request);

        result.Success.Should().BeTrue();
        result.Retryable.Should().BeFalse();
        result.Code.Should().Be("FORWARDED");
        result.ConsentStatusAfterHandling.Should().Be(ConsentStatus.OptIn);
        await outcomeRepository.Received(1).SaveAsync(Arg.Is<NonStopForwardingOutcomeRecord>(x => x.Success && !x.Retryable && x.Code == "FORWARDED"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenApplicationEndpointUnavailable_ReturnsRetryableFailure()
    {
        var callbackClient = Substitute.For<IApplicationCallbackClient>();
        callbackClient.ForwardAsync(Arg.Any<NonStopForwardingRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ApplicationCallbackResult>(new ApplicationEndpointUnavailableException("endpoint unavailable")));
        var outcomeRepository = Substitute.For<IForwardingOutcomeRepository>();
        var handler = new NonStopForwardingHandler(callbackClient, outcomeRepository);

        var request = CreateRequest(NormalizedIntent.Help, ConsentStatus.OptOut);
        var result = await handler.HandleAsync(request);

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        result.Code.Should().Be("APP_ENDPOINT_UNAVAILABLE");
        result.ConsentStatusAfterHandling.Should().Be(ConsentStatus.OptOut);
        await outcomeRepository.Received(1).SaveAsync(Arg.Is<NonStopForwardingOutcomeRecord>(x => !x.Success && x.Retryable && x.Code == "APP_ENDPOINT_UNAVAILABLE"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithOtherIntent_DoesNotMutateConsentState()
    {
        var callbackClient = Substitute.For<IApplicationCallbackClient>();
        callbackClient.ForwardAsync(Arg.Any<NonStopForwardingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationCallbackResult(true, "ok"));
        var outcomeRepository = Substitute.For<IForwardingOutcomeRepository>();
        var handler = new NonStopForwardingHandler(callbackClient, outcomeRepository);

        var request = CreateRequest(NormalizedIntent.Other, ConsentStatus.OptOut);
        var result = await handler.HandleAsync(request);

        result.Success.Should().BeTrue();
        result.ConsentStatusAfterHandling.Should().Be(ConsentStatus.OptOut);
        await callbackClient.Received(1).ForwardAsync(Arg.Any<NonStopForwardingRequest>(), Arg.Any<CancellationToken>());
    }

    private static NonStopForwardingRequest CreateRequest(NormalizedIntent intent, ConsentStatus consentStatus)
    {
        return new NonStopForwardingRequest(
            EventId: "evt-456",
            SourceApplication: "BizTalk",
            CustomerPhoneNumber: "+14045550100",
            MessageText: intent == NormalizedIntent.Help ? "HELP" : "Hello",
            Intent: intent,
            ConsentStatusBeforeHandling: consentStatus);
    }
}
