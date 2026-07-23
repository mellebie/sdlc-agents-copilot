using Microsoft.AspNetCore.Mvc;
using TCPA.Api.Filters;
using TCPA.Api.Messaging;
using TCPA.Api.Models;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;

namespace TCPA.Api.Controllers;

/// <summary>
/// Receives inbound SMS messages from Cool Text / Twilio.
/// Returns HTTP 200 immediately (within the 5-second SLA); all message processing
/// is handed off asynchronously via Kafka (SPEC-001, BR-001, BR-002, BR-003).
/// </summary>
[ApiController]
[Route("webhook")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class InboundWebhookController : ControllerBase
{
    private readonly ICoolTextAccountRepository _coolTextRepo;
    private readonly IProcessedMessageRepository _processedRepo;
    private readonly IMessagePublisher _publisher;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<InboundWebhookController> _logger;

    public InboundWebhookController(
        ICoolTextAccountRepository coolTextRepo,
        IProcessedMessageRepository processedRepo,
        IMessagePublisher publisher,
        IPhoneNumberHasher hasher,
        ILogger<InboundWebhookController> logger)
    {
        _coolTextRepo = coolTextRepo;
        _processedRepo = processedRepo;
        _publisher = publisher;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>
    /// Receives an inbound SMS event from Cool Text or Twilio.
    /// Validates the destination account, enforces idempotency, and publishes
    /// the event to Kafka for async processing.
    /// </summary>
    /// <param name="request">Inbound webhook payload from the SMS provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with internal tracking ID on success; 400 on unknown/inactive account; 500 on Kafka failure.</returns>
    [HttpPost("inbound")]
    [ProducesResponseType(typeof(InboundWebhookResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReceiveInbound([FromBody] InboundWebhookRequest request, CancellationToken ct)
    {
        // BR-001 / SPEC-001: validate destination is a known, active Cool Text account
        var account = await _coolTextRepo.GetByAccountNumberAsync(request.To, ct);
        if (account is null || !account.IsActive)
        {
            _logger.LogWarning("Inbound webhook rejected: unrecognised or inactive account {ToHash}", _hasher.Hash(request.To));
            return BadRequest(new { error = $"Cool Text account '{request.To}' is not registered or inactive." });
        }

        // BR-003 edge case: enforce idempotency — duplicate messageId returns the original response
        var existing = await _processedRepo.FindAsync(request.MessageId, "webhook", ct);
        if (existing is not null)
        {
            _logger.LogInformation("Duplicate inbound messageId {MessageId} — returning original response", request.MessageId);
            return Ok(new InboundWebhookResponse("received", existing.InternalId.ToString()));
        }

        var internalId = Guid.NewGuid();

        // Publish to Kafka for async processing; return 500 if the broker is unreachable
        try
        {
            await _publisher.PublishInboundAsync(new InboundMessageEvent(
                InternalId: internalId.ToString(),
                MessageId: request.MessageId,
                From: request.From,
                To: request.To,
                Body: request.Body,
                Provider: request.Provider,
                CoolTextAccountNumber: account.AccountNumber,
                ApplicationId: account.ApplicationId,
                Timestamp: request.Timestamp), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish inbound message {MessageId} to Kafka", request.MessageId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to queue message for processing." });
        }

        // Record the message so any retry of the same messageId gets the idempotent response
        await _processedRepo.AddAsync(new ProcessedMessage
        {
            MessageId = request.MessageId,
            InternalId = internalId,
            ResponseStatus = "received",
            ProcessedAt = DateTime.UtcNow,
            Endpoint = "webhook"
        }, ct);

        // Structured log — phone number hashed per policy (never log raw PII)
        _logger.LogInformation("{EventType} inbound message {MessageId} from {PhoneHash}",
            LogEventTypes.MessageQueued,
            request.MessageId,
            _hasher.Hash(request.From));

        return Ok(new InboundWebhookResponse("received", internalId.ToString()));
    }
}
