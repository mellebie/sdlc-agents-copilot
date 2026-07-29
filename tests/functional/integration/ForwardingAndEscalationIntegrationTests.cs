using ConsentService.Models;
using ConsentService.Services;
using FluentAssertions;
using IntentClassifier.Handlers;
using IntentClassifier.Services;
using NSubstitute;
using Xunit;

namespace FunctionalTests.Integration;

public sealed class ForwardingAndEscalationIntegrationTests
{
    [Fact]
    public async Task HelpForwarding_EndpointUnavailable_IsRetryableAndEscalationSafe()
    {
        var callbackClient = Substitute.For<IApplicationCallbackClient>();
        callbackClient.ForwardAsync(Arg.Any<NonStopForwardingRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ApplicationCallbackResult>(new ApplicationEndpointUnavailableException("simulated outage")));

        var repo = Substitute.For<IForwardingOutcomeRepository>();
        var handler = new NonStopForwardingHandler(callbackClient, repo);

        var result = await handler.HandleAsync(new NonStopForwardingRequest(
            EventId: "evt-int-1",
            SourceApplication: "BizTalk",
            CustomerPhoneNumber: "+14045550200",
            MessageText: "HELP",
            Intent: NormalizedIntent.Help,
            ConsentStatusBeforeHandling: ConsentStatus.OptIn));

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        result.Code.Should().Be("APP_ENDPOINT_UNAVAILABLE");
    }
}
