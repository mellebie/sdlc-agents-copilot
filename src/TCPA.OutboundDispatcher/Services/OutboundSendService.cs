using System.Text.Json;
using Microsoft.Extensions.Logging;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.OutboundDispatcher.Messaging;

namespace TCPA.OutboundDispatcher.Services;

/// <summary>
/// Sends an authorized outbound SMS via <see cref="ICoolTextApiClient"/> with up to 3 retries
/// (exponential back-off: 2 s → 4 s → 8 s). Writes <see cref="AuditEventType.OutboundDelivered"/>
/// on success or <see cref="AuditEventType.OutboundFailed"/> when all retries are exhausted.
///
/// Never throws — all errors are caught, logged at Critical, and recorded as OutboundFailed
/// so the caller can commit the Kafka offset and unblock the partition.
///
/// The <c>internal</c> <see cref="SendAsync_WithDelays"/> overload allows tests to inject
/// zero-duration delays to exercise retry logic without real waits.
/// </summary>
public class OutboundSendService : IOutboundSendService
{
    private static readonly TimeSpan[] ProductionRetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    private readonly TcpaDbContext _ctx;
    private readonly ICoolTextApiClient _coolTextClient;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<OutboundSendService> _logger;

    /// <summary>Initializes a new instance of <see cref="OutboundSendService"/>.</summary>
    public OutboundSendService(
        TcpaDbContext ctx,
        ICoolTextApiClient coolTextClient,
        IAuditLogRepository auditRepo,
        IPhoneNumberHasher hasher,
        ILogger<OutboundSendService> logger)
    {
        _ctx = ctx;
        _coolTextClient = coolTextClient;
        _auditRepo = auditRepo;
        _hasher = hasher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task SendAsync(OutboundMessageEvent @event, CancellationToken ct)
        => SendAsync_WithDelays(@event, ProductionRetryDelays, ct);

    /// <summary>
    /// Public overload with injectable retry delays. Tests pass
    /// <c>[TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]</c> to exercise the full retry
    /// and audit logic without real waits.
    /// </summary>
    public async Task SendAsync_WithDelays(
        OutboundMessageEvent @event,
        TimeSpan[] retryDelays,
        CancellationToken ct)
    {
        var phoneHash = _hasher.Hash(@event.ToNumber);
        CoolTextSendResult? lastResult = null;
        Exception? lastException = null;

        // Attempt 0 (initial) through retryDelays.Length (inclusive) = retryDelays.Length + 1 total attempts
        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(retryDelays[attempt - 1], ct);

            try
            {
                lastResult = await _coolTextClient.SendSmsAsync(
                    @event.ToNumber, @event.CoolTextAccountNumber, @event.Body, ct);

                if (lastResult.Success)
                    break;

                _logger.LogWarning(
                    "Cool Text returned failure on attempt {Attempt}/{Total} for {PhoneHash}: {Error}",
                    attempt + 1, retryDelays.Length + 1, phoneHash, lastResult.ErrorMessage);
            }
            catch (OperationCanceledException)
            {
                throw; // Propagate graceful shutdown — never swallow cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Cool Text threw on attempt {Attempt}/{Total} for {PhoneHash}",
                    attempt + 1, retryDelays.Length + 1, phoneHash);
            }
        }

        if (lastResult?.Success == true)
        {
            _auditRepo.Write(new AuditLog
            {
                EventType = AuditEventType.OutboundDelivered,
                PhoneNumber = phoneHash,
                OccurredAt = DateTime.UtcNow,
                ApplicationId = @event.ApplicationId,
                MessageId = @event.MessageId,
                Details = JsonSerializer.Serialize(new
                {
                    providerMessageId = lastResult.MessageId,
                    coolTextAccountNumber = @event.CoolTextAccountNumber
                })
            });
            await _ctx.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Outbound message delivered. PhoneHash={PhoneHash} MessageId={MessageId} ProviderMessageId={ProviderMessageId}",
                phoneHash, @event.MessageId, lastResult.MessageId);
        }
        else
        {
            var reason = lastException?.Message ?? lastResult?.ErrorMessage ?? "Unknown";
            _logger.LogCritical(
                "Outbound send failed after all retries for {PhoneHash}: {Reason}",
                phoneHash, reason);

            _auditRepo.Write(new AuditLog
            {
                EventType = AuditEventType.OutboundFailed,
                PhoneNumber = phoneHash,
                OccurredAt = DateTime.UtcNow,
                ApplicationId = @event.ApplicationId,
                MessageId = @event.MessageId,
                Details = JsonSerializer.Serialize(new
                {
                    reason,
                    coolTextAccountNumber = @event.CoolTextAccountNumber,
                    retriesAttempted = retryDelays.Length
                })
            });
            await _ctx.SaveChangesAsync(ct);
        }
    }
}
