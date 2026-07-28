using FluentAssertions;
using IntakeApi.Contracts;
using IntakeApi.Controllers;
using IntakeApi.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace IntakeApi.Tests.Controllers;

public sealed class InboundMessagesControllerTests
{
    private const string FixedCorrelationId = "corr-123";

    [Fact]
    public void Post_WithValidRequest_ReturnsAcceptedResponse()
    {
        var request = CreateValidRequest();
        var validator = new InboundMessageRequestValidator();
        var routingService = CreateRouteableRoutingService();
        var controller = new InboundMessagesController(new FixedCorrelationIdGenerator(FixedCorrelationId), routingService, validator);

        ActionResult<InboundMessageAcceptedResponse> actionResult = controller.Post(request);

        var ok = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var payload = ok.Value.Should().BeOfType<InboundMessageAcceptedResponse>().Subject;
        payload.Accepted.Should().BeTrue();
        payload.ClassificationState.Should().Be("PENDING");
        payload.CorrelationId.Should().Be(FixedCorrelationId);
    }

    [Fact]
    public void Post_WithInvalidPhoneNumber_ReturnsStructuredValidationError()
    {
        var request = new InboundMessageRequest
        {
            EventId = "evt-001",
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "4045550100",
            SourceLdc = SourceLdc.Vng,
            SourceApplication = SourceApplication.BizTalk,
            CoolTextAccountId = "acct-001",
            MessageText = "STOP"
        };
        var validator = new InboundMessageRequestValidator();
        var routingService = CreateRouteableRoutingService();
        var controller = new InboundMessagesController(new FixedCorrelationIdGenerator(FixedCorrelationId), routingService, validator);

        ActionResult<InboundMessageAcceptedResponse> actionResult = controller.Post(request);

        var badRequest = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        var payload = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        payload.Code.Should().Be("INVALID_INPUT");
        payload.CorrelationId.Should().Be(FixedCorrelationId);
        payload.Message.Should().Contain("E.164");
    }

    [Fact]
    public void Post_WithMissingRequiredFields_ReturnsStructuredValidationError()
    {
        var request = new InboundMessageRequest
        {
            EventId = "",
            ReceivedAtUtc = null,
            CustomerPhoneNumber = null,
            SourceLdc = SourceLdc.Unknown,
            SourceApplication = SourceApplication.Unknown,
            CoolTextAccountId = null,
            MessageText = ""
        };
        var validator = new InboundMessageRequestValidator();
        var routingService = CreateRouteableRoutingService();
        var controller = new InboundMessagesController(new FixedCorrelationIdGenerator(FixedCorrelationId), routingService, validator);

        ActionResult<InboundMessageAcceptedResponse> actionResult = controller.Post(request);

        var badRequest = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        var payload = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        payload.Code.Should().Be("INVALID_INPUT");
        payload.CorrelationId.Should().Be(FixedCorrelationId);
        payload.Message.Should().Contain("Event identifier is required.");
        payload.Message.Should().Contain("Source LDC is required.");
        payload.Message.Should().Contain("Source application is required.");
    }

    [Fact]
    public void Post_WithOutOfScopeMapping_ReturnsNotFound()
    {
        var request = new InboundMessageRequest
        {
            EventId = "evt-001",
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "+14045550100",
            SourceLdc = SourceLdc.Vng,
            SourceApplication = SourceApplication.BizTalk,
            CoolTextAccountId = "acct-999",
            MessageText = "STOP"
        };
        var validator = new InboundMessageRequestValidator();
        var routingService = new RoutingEligibilityService(new ScopeMappingResolver());
        var controller = new InboundMessagesController(new FixedCorrelationIdGenerator(FixedCorrelationId), routingService, validator);

        ActionResult<InboundMessageAcceptedResponse> actionResult = controller.Post(request);

        var notFound = actionResult.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);
        var payload = notFound.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        payload.Code.Should().Be("SCOPE_MAPPING_NOT_FOUND");
        payload.CorrelationId.Should().Be(FixedCorrelationId);
        payload.Message.Should().Contain("REJECTED_OUT_OF_SCOPE");
    }

    private static InboundMessageRequest CreateValidRequest() =>
        new()
        {
            EventId = "evt-001",
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "+14045550100",
            SourceLdc = SourceLdc.Vng,
            SourceApplication = SourceApplication.BizTalk,
            CoolTextAccountId = "acct-001",
            MessageText = "STOP"
        };

    private static IRoutingEligibilityService CreateRouteableRoutingService()
    {
        var substitute = Substitute.For<IRoutingEligibilityService>();
        substitute.Evaluate(Arg.Any<InboundMessageRequest>())
            .Returns(new RoutingEligibilityResult(true, "ROUTEABLE", "test"));
        return substitute;
    }

    private sealed class FixedCorrelationIdGenerator : ICorrelationIdGenerator
    {
        private readonly string _correlationId;

        public FixedCorrelationIdGenerator(string correlationId)
        {
            _correlationId = correlationId;
        }

        public string Generate() => _correlationId;
    }
}
