using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Models;
using TCPA.Api.Services.SmsProxy;

namespace TCPA.Api.Controllers;

/// <summary>
/// Receives inbound SMS webhooks from the Cool Text platform and routes them to
/// the inbound SMS processing pipeline (SPEC-002, SPEC-003, SPEC-004, SPEC-005).
///
/// Security: Every request must carry a valid HMAC-SHA256 signature in the
/// configured signature header (ADR-007). Requests with missing or invalid
/// signatures are rejected with 401 Unauthorized before any processing occurs.
///
/// Webhook acknowledgement contract: Cool Text expects a 200 OK response to
/// acknowledge receipt. The 200 is returned immediately, before opt-out processing
/// or application callback dispatch, to prevent Cool Text from timing out and
/// retrying the webhook delivery. Downstream processing is dispatched as a
/// fire-and-forget background task after the response is sent.
/// </summary>
[ApiController]
[Route("api/v1/sms")]
public sealed class InboundSmsController : ControllerBase
{
    private readonly ICoolTextWebhookValidator _webhookValidator;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<InboundSmsController> _logger;

    /// <summary>
    /// Initializes the inbound SMS controller with webhook validation and routing dependencies.
    /// </summary>
    public InboundSmsController(
        ICoolTextWebhookValidator webhookValidator,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<InboundSmsController> logger)
    {
        _webhookValidator = webhookValidator ?? throw new ArgumentNullException(nameof(webhookValidator));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Receives an inbound SMS webhook from Cool Text, validates the HMAC-SHA256 signature,
    /// acknowledges receipt with 200 OK, then asynchronously processes the message.
    ///
    /// Processing (async, after response): keyword detection → opt-out pipeline (if applicable)
    /// → application callback forwarding.
    ///
    /// Authentication: HMAC-SHA256 signature validated from the configured signature header.
    /// Returns 401 Unauthorized immediately if the signature is absent or does not match.
    ///
    /// The 200 OK response body is <c>{"received":true}</c> per the Cool Text webhook contract.
    /// </summary>
    /// <param name="message">Inbound SMS webhook payload from Cool Text.</param>
    /// <returns>
    /// 200 OK with <see cref="InboundAcknowledgement"/> body on success.
    /// 400 Bad Request for malformed payloads.
    /// 401 Unauthorized when the HMAC signature is missing or invalid.
    /// </returns>
    [HttpPost("inbound")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InboundAcknowledgement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceiveInbound([FromBody] InboundSmsMessage message)
    {
        // Validate model binding — [Required] attributes on InboundSmsMessage enforce required fields.
        if (!ModelState.IsValid)
        {
            _logger.LogWarning(
                "Inbound webhook rejected: invalid payload. CorrelationId: {CorrelationId}.",
                HttpContext.TraceIdentifier);
            return BadRequest(ModelState);
        }

        // HMAC-SHA256 signature validation MUST occur before any processing (ADR-007, STORY-004).
        // Read the raw body that was buffered by EnableBuffering middleware before model binding.
        var rawBody = await ReadRawBodyAsync();
        var signatureHeaderValue = Request.Headers[_webhookValidator.SignatureHeaderName].FirstOrDefault();

        if (!_webhookValidator.IsSignatureValid(rawBody, signatureHeaderValue))
        {
            // Security event logged inside the validator at Warning level.
            // Log the correlation ID here for forensic linkage without logging the signature value.
            _logger.LogWarning(
                "Inbound webhook rejected: HMAC signature invalid or missing. " +
                "CorrelationId: {CorrelationId}. Account: {AccountId}.",
                HttpContext.TraceIdentifier,
                message.CoolTextAccountId);

            return Unauthorized();
        }

        _logger.LogInformation(
            "Inbound webhook received and signature validated. " +
            "Account: {AccountId}. MessageId: {MessageId}. CorrelationId: {CorrelationId}.",
            message.CoolTextAccountId,
            message.CoolTextMessageId,
            HttpContext.TraceIdentifier);

        // Return 200 OK to Cool Text immediately — before any downstream processing —
        // to prevent Cool Text from treating this as a timed-out delivery and retrying.
        // Processing runs as a fire-and-forget background task using CancellationToken.None
        // so it is not tied to the HTTP request lifetime.
        _ = ProcessInboundAsync(message);

        return Ok(InboundAcknowledgement.Instance);
    }

    /// <summary>
    /// Dispatches inbound SMS processing in a fresh DI scope after the HTTP response has been
    /// sent. A new scope is required because the request scope is disposed when the HTTP
    /// response completes — resolving scoped services (DbContext, IAuditLogService) from the
    /// request scope in a fire-and-forget task causes ObjectDisposedException (CR-004).
    /// </summary>
    private async Task ProcessInboundAsync(InboundSmsMessage message)
    {
        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        IInboundSmsHandler handler = scope.ServiceProvider.GetRequiredService<IInboundSmsHandler>();
        try
        {
            await handler.HandleAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception in inbound SMS background processing. " +
                "Account: {AccountId}. MessageId: {MessageId}.",
                message.CoolTextAccountId,
                message.CoolTextMessageId);
        }
    }

    /// <summary>
    /// Reads the raw request body as a UTF-8 string for HMAC-SHA256 signature computation.
    /// Requires that the request body was buffered by calling <c>Request.EnableBuffering()</c>
    /// in middleware before ASP.NET Core model binding consumed the stream.
    /// </summary>
    private async Task<string> ReadRawBodyAsync()
    {
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;
        return rawBody;
    }
}
