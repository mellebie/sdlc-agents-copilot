// Tests for OutboundSmsController
// Source: TASK (SMS Proxy & Routing) | SPEC-001, NFS-005
// Covers: FORWARDED/SUPPRESSED/503/400 responses, E.164 validation

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Controllers;
using TCPA.Api.Models;
using TCPA.Api.Services.SmsProxy;
using Xunit;

namespace TCPA.Api.Tests.Unit.Controllers;

public sealed class OutboundSmsControllerTests
{
    private readonly Mock<IOutboundSmsGate> _outboundSmsGate;
    private readonly OutboundSmsController _sut;

    private static readonly OutboundSmsRequest ValidRequest = new()
    {
        CoolTextAccountId = "ACC-001",
        DestinationCellNumber = "+12025551234",
        MessageBody = "Your appointment is confirmed."
    };

    public OutboundSmsControllerTests()
    {
        _outboundSmsGate = new Mock<IOutboundSmsGate>();

        _sut = new OutboundSmsController(
            _outboundSmsGate.Object,
            new Mock<ILogger<OutboundSmsController>>().Object);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Should_Return200WithForwardedStatus_When_GateDecisionIsAllowed()
    {
        // Arrange
        _outboundSmsGate
            .Setup(g => g.ProcessAsync(ValidRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OutboundGateResult.Forwarded("CT-MSG-001"));

        // Act
        var result = await _sut.SendOutbound(ValidRequest);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var response = okResult.Value.Should().BeOfType<SmsResponse>().Subject;
        response.Status.Should().Be(SmsStatus.Forwarded);
        response.MessageId.Should().Be("CT-MSG-001");
    }

    [Fact]
    public async Task Should_Return200WithSuppressedStatus_When_GateDecisionIsBlocked()
    {
        // Arrange — 200 OK is correct for SUPPRESSED; suppression is not an error (SPEC-001)
        _outboundSmsGate
            .Setup(g => g.ProcessAsync(ValidRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OutboundGateResult.Suppressed());

        // Act
        var result = await _sut.SendOutbound(ValidRequest);

        // Assert — status code must be 200, not 4xx or 5xx
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var response = okResult.Value.Should().BeOfType<SmsResponse>().Subject;
        response.Status.Should().Be(SmsStatus.Suppressed);
        response.SuppressionReason.Should().Be("OPT_OUT");
        response.MessageId.Should().BeNull();
    }

    [Fact]
    public async Task Should_Return200WithUnregisteredAccountStatus_When_GateDecisionIsUnregistered()
    {
        // Arrange
        _outboundSmsGate
            .Setup(g => g.ProcessAsync(ValidRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OutboundGateResult.UnregisteredAccount());

        // Act
        var result = await _sut.SendOutbound(ValidRequest);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var response = okResult.Value.Should().BeOfType<SmsResponse>().Subject;
        response.Status.Should().Be(SmsStatus.UnregisteredAccount);
    }

    [Fact]
    public async Task Should_Return503_When_GateThrowsOutboundGateUnavailableException()
    {
        // Arrange — fail-closed: database unavailable means 503 (NFS-005)
        _outboundSmsGate
            .Setup(g => g.ProcessAsync(ValidRequest, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutboundGateUnavailableException("TCPA opt-out status unavailable."));

        // Act
        var result = await _sut.SendOutbound(ValidRequest);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);

        var error = statusResult.Value.Should().BeOfType<SmsErrorResponse>().Subject;
        error.Error.Should().Be("SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Should_Return400_When_DestinationCellNumberIsNotE164()
    {
        // Arrange — inject invalid E.164 format error into model state
        _sut.ModelState.AddModelError("destination_cell_number",
            "destination_cell_number must be in E.164 format (e.g., +12025551234).");

        var invalidRequest = new OutboundSmsRequest
        {
            CoolTextAccountId = "ACC-001",
            DestinationCellNumber = "not-a-phone-number",
            MessageBody = "Test message"
        };

        // Act
        var result = await _sut.SendOutbound(invalidRequest);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var error = badRequestResult.Value.Should().BeOfType<SmsErrorResponse>().Subject;
        error.Error.Should().Be("VALIDATION_ERROR");
        error.Fields.Should().Contain("destination_cell_number");

        // Gate must NOT be invoked for invalid requests
        _outboundSmsGate.Verify(
            g => g.ProcessAsync(It.IsAny<OutboundSmsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Return400_When_RequiredFieldsAreMissing()
    {
        // Arrange — simulate model binding failure for missing required fields
        _sut.ModelState.AddModelError("cool_text_account_id", "The cool_text_account_id field is required.");
        _sut.ModelState.AddModelError("message_body", "The message_body field is required.");

        var incompleteRequest = new OutboundSmsRequest
        {
            CoolTextAccountId = "",
            DestinationCellNumber = "+12025551234",
            MessageBody = ""
        };

        // Act
        var result = await _sut.SendOutbound(incompleteRequest);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var error = badRequestResult.Value.Should().BeOfType<SmsErrorResponse>().Subject;
        error.Error.Should().Be("VALIDATION_ERROR");
        error.Fields.Should().HaveCount(2);
    }
}
