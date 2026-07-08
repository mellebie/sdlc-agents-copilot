// Tests for InboundSmsController
// Source: TASK (SMS Proxy & Routing) | SPEC-002, ADR-007
// Covers: HMAC validation gate, 200/401/400 responses, fire-and-forget dispatch

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using TCPA.Api.Controllers;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Models;
using TCPA.Api.Services.SmsProxy;
using Xunit;

namespace TCPA.Api.Tests.Unit.Controllers;

public sealed class InboundSmsControllerTests
{
    private readonly Mock<ICoolTextWebhookValidator> _webhookValidator;
    private readonly Mock<IInboundSmsHandler> _inboundSmsHandler;
    private readonly InboundSmsController _sut;

    private static readonly InboundSmsMessage ValidMessage = new()
    {
        CoolTextAccountId = "ACC-001",
        SenderCellNumber = "+12025551234",
        MessageBody = "Hello",
        CoolTextMessageId = "MSG-001"
    };

    public InboundSmsControllerTests()
    {
        _webhookValidator = new Mock<ICoolTextWebhookValidator>();
        _inboundSmsHandler = new Mock<IInboundSmsHandler>();

        _webhookValidator
            .Setup(v => v.SignatureHeaderName)
            .Returns(CoolTextWebhookValidator.DefaultSignatureHeader);

        _sut = new InboundSmsController(
            _webhookValidator.Object,
            _inboundSmsHandler.Object,
            new Mock<ILogger<InboundSmsController>>().Object);

        // Set up a default HTTP context with a bufferable body
        SetupHttpContext(_sut, "{\"cool_text_account_id\":\"ACC-001\",\"sender_cell_number\":\"+12025551234\",\"message_body\":\"Hello\",\"cool_text_message_id\":\"MSG-001\"}");
    }

    private static void SetupHttpContext(InboundSmsController controller, string rawBody, string? signatureHeader = null)
    {
        var httpContext = new DefaultHttpContext();
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        var bodyStream = new MemoryStream(bodyBytes);
        httpContext.Request.Body = bodyStream;
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bodyBytes.Length;

        if (signatureHeader is not null)
        {
            httpContext.Request.Headers[CoolTextWebhookValidator.DefaultSignatureHeader] = signatureHeader;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task Should_Return200OK_When_HmacSignatureIsValid()
    {
        // Arrange
        _webhookValidator
            .Setup(v => v.IsSignatureValid(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        _inboundSmsHandler
            .Setup(h => h.HandleAsync(It.IsAny<InboundSmsMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ReceiveInbound(ValidMessage);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        okResult.Value.Should().BeOfType<InboundAcknowledgement>();
        ((InboundAcknowledgement)okResult.Value!).Received.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return401_When_HmacSignatureIsInvalid()
    {
        // Arrange
        _webhookValidator
            .Setup(v => v.IsSignatureValid(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(false);

        // Act
        var result = await _sut.ReceiveInbound(ValidMessage);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>()
            .Which.StatusCode.Should().Be(401);

        // Handler must NOT be called after rejecting the signature
        _inboundSmsHandler.Verify(
            h => h.HandleAsync(It.IsAny<InboundSmsMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Return401_When_SignatureHeaderIsMissing()
    {
        // Arrange — no signature header value means IsSignatureValid receives null
        _webhookValidator
            .Setup(v => v.IsSignatureValid(It.IsAny<string>(), null))
            .Returns(false);

        // Act
        var result = await _sut.ReceiveInbound(ValidMessage);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Should_Return400_When_ModelStateIsInvalid()
    {
        // Arrange — simulate model binding failure by invalidating model state
        _sut.ModelState.AddModelError("sender_cell_number", "The sender_cell_number field is required.");

        // Act
        var result = await _sut.ReceiveInbound(new InboundSmsMessage
        {
            CoolTextAccountId = "ACC-001",
            SenderCellNumber = "", // missing required field
            MessageBody = "STOP",
            CoolTextMessageId = "MSG-001"
        });

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);

        // Signature validation must NOT be attempted for malformed payloads
        _webhookValidator.Verify(
            v => v.IsSignatureValid(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Return200Immediately_Before_BackgroundProcessingCompletes()
    {
        // Arrange — handler is deliberately slow; the controller must NOT await it
        var handlerCompletionSource = new TaskCompletionSource();

        _webhookValidator
            .Setup(v => v.IsSignatureValid(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(true);

        _inboundSmsHandler
            .Setup(h => h.HandleAsync(It.IsAny<InboundSmsMessage>(), It.IsAny<CancellationToken>()))
            .Returns(handlerCompletionSource.Task);

        // Act — should complete immediately, returning 200, without waiting for handler
        var resultTask = _sut.ReceiveInbound(ValidMessage);

        // The result should resolve without needing to complete the handler
        var result = await resultTask;

        // Assert — 200 returned before handler completes
        result.Should().BeOfType<OkObjectResult>();

        // Clean up the dangling task
        handlerCompletionSource.SetResult();
    }
}
