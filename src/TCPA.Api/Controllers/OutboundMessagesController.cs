using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TCPA.Api.Filters;
using TCPA.Api.Messaging;
using TCPA.Api.Models;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;

namespace TCPA.Api.Controllers;

/// <summary>
/// Accepts outbound SMS submission requests from gas applications.
/// Performs queue-time opt-out check and publishes approved messages to Kafka (SPEC-006, SPEC-007, BR-018-BR-023).
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class OutboundMessagesController : ControllerBase
{
    private readonly ICoolTextAccountRepository _coolTextRepo;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IProcessedMessageRepository _processedRepo;
    private readonly IMessagePublisher _publisher;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<OutboundMessagesController> _logger;

    public OutboundMessagesController(
        ICoolTextAccountRepository coolTextRepo,
        IOptOutStatusRepository statusRepo,
        IProcessedMessageRepository processedRepo,
        IMessagePublisher publisher,
        IPhoneNumberHasher hasher,
        ILogger<OutboundMessagesController> logger)
    {
        _coolTextRepo = coolTextRepo;
        _statusRepo = statusRepo;
        _processedRepo = processedRepo;
        _publisher = publisher;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>
    /// Submits an outbound SMS for dispatch. Validates the caller's Cool Text account,
    /// enforces idempotency via correlationId, performs a queue-time opt-out check,
    /// and publishes approved messages to Kafka.
    /// </summary>
    /// <param name="request">Outbound message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 with status "queued" or "suppressed"; 400 for unknown/inactive account;
    /// 401 when API key is missing or invalid; 503 when compliance check or messaging service is unavailable.
    /// </returns>
    [HttpPost("outbound")]
    [ProducesResponseType(typeof(OutboundMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SubmitOutbound([FromBody] OutboundMessageRequest request, CancellationToken ct)
    {
        // BR-019: validate the submitting application is a registered, active Cool Text account
        var account = await _coolTextRepo.GetByAccountNumberAsync(request.CoolTextAccountNumber, ct);
        if (account is null || !account.IsActive)
        {
            return BadRequest(new { error = $"Cool Text account '{request.CoolTextAccountNumber}' is not registered or inactive." });
        }

        // BR-018: idempotency — if a correlationId is supplied and already processed, return original response
        if (!string.IsNullOrEmpty(request.CorrelationId))
        {
            var existing = await _processedRepo.FindAsync(request.CorrelationId, "outbound", ct);
            if (existing is not null)
            {
                _logger.LogInformation("Duplicate correlationId {CorrelationId} — returning original response", request.CorrelationId);
                return Ok(new OutboundMessageResponse(
                    existing.ResponseStatus,
                    existing.ResponseStatus == "queued" ? existing.InternalId.ToString() : null,
                    existing.ResponseStatus == "queued" ? (DateTimeOffset?)existing.ProcessedAt : null,
                    existing.ResponseStatus == "suppressed" ? "opted-out" : null));
            }
        }

        // SPEC-007, BR-021: queue-time opt-out check — fail-safe: if status store is unavailable, do NOT send
        bool isOptedOut;
        try
        {
            isOptedOut = await _statusRepo.IsOptedOutAsync(request.ToNumber, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Opt-out status store unavailable — suppressing send for {PhoneHash} (fail-safe)",
                _hasher.Hash(request.ToNumber));
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "TCPA compliance check unavailable. Message not sent." });
        }

        if (isOptedOut)
        {
            // BR-023: log suppression — phone number hashed per PII policy
            _logger.LogInformation("{EventType} {PhoneHash} opted-out at queue time",
                LogEventTypes.MessageSuppressed, _hasher.Hash(request.ToNumber));

            var suppressionKey = request.CorrelationId ?? Guid.NewGuid().ToString();
            try
            {
                await _processedRepo.AddAsync(new ProcessedMessage
                {
                    MessageId = suppressionKey,
                    InternalId = Guid.NewGuid(),
                    ResponseStatus = "suppressed",
                    ProcessedAt = DateTime.UtcNow,
                    Endpoint = "outbound"
                }, ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request already wrote the idempotency record for the same correlationId.
                _logger.LogDebug("Concurrent duplicate correlationId {CorrelationId} on suppressed path — idempotency record already written", suppressionKey);
            }

            return Ok(new OutboundMessageResponse("suppressed", null, null, "opted-out"));
        }

        // Publish to Kafka for dispatch; return 503 if broker is unreachable (BR-022)
        var messageId = Guid.NewGuid();
        var queuedAt = DateTimeOffset.UtcNow;

        try
        {
            await _publisher.PublishOutboundAsync(new OutboundMessageEvent(
                MessageId: messageId.ToString(),
                ToNumber: request.ToNumber,
                Body: request.Body,
                CoolTextAccountNumber: request.CoolTextAccountNumber,
                ApplicationId: request.ApplicationId,
                CorrelationId: request.CorrelationId,
                QueuedAt: queuedAt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish outbound message to Kafka — returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Messaging service unavailable. Retry after a moment." });
        }

        // Record for idempotency; prefer correlationId as key when present.
        // Guard against a concurrent duplicate that also passed FindAsync before either wrote the record.
        var idempotencyKey = request.CorrelationId ?? messageId.ToString();
        try
        {
            await _processedRepo.AddAsync(new ProcessedMessage
            {
                MessageId = idempotencyKey,
                InternalId = messageId,
                ResponseStatus = "queued",
                ProcessedAt = queuedAt.UtcDateTime,
                Endpoint = "outbound"
            }, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request for the same correlationId already wrote the idempotency record.
            // The Kafka publish already succeeded, so return 200 as normal.
            _logger.LogDebug("Concurrent duplicate correlationId {CorrelationId} on queued path — idempotency record already written", idempotencyKey);
        }

        // Structured log — phone number hashed per PII policy (never log raw number at INFO/WARN/ERROR)
        _logger.LogInformation("{EventType} outbound message {MessageId} for {PhoneHash}",
            LogEventTypes.MessageQueued, messageId, _hasher.Hash(request.ToNumber));

        return Ok(new OutboundMessageResponse("queued", messageId.ToString(), queuedAt, null));
    }
}
