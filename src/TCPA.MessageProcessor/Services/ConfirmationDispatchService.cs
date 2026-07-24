using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;

namespace TCPA.MessageProcessor.Services;

/// <summary>
/// Reads the opt-out message body from SystemConfig, sends it via ICoolTextApiClient with
/// up to 3 retries (2s/4s/8s exponential backoff), and writes ConfirmationDispatched,
/// ConfirmationFailed, or SlaBreach audit entries. Never throws — all errors are caught,
/// logged at Critical level, and recorded as ConfirmationFailed audit entries.
/// </summary>
public class ConfirmationDispatchService : IConfirmationDispatchService
{
    private static readonly TimeSpan[] ProductionRetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    private static readonly TimeSpan SlaThreshold = TimeSpan.FromSeconds(60);

    private readonly TcpaDbContext _ctx;
    private readonly ISystemConfigRepository _configRepo;
    private readonly ICoolTextApiClient _coolTextClient;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<ConfirmationDispatchService> _logger;

    public ConfirmationDispatchService(
        [FromKeyedServices("primary")] TcpaDbContext ctx,
        ISystemConfigRepository configRepo,
        ICoolTextApiClient coolTextClient,
        IAuditLogRepository auditRepo,
        IPhoneNumberHasher hasher,
        ILogger<ConfirmationDispatchService> logger)
    {
        _ctx = ctx;
        _configRepo = configRepo;
        _coolTextClient = coolTextClient;
        _auditRepo = auditRepo;
        _hasher = hasher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task DispatchConfirmationAsync(
        string phoneNumber,
        string coolTextAccountNumber,
        DateTimeOffset receivedAt,
        long auditRecordId,
        CancellationToken ct)
        => DispatchConfirmationAsync_WithDelays(
            phoneNumber, coolTextAccountNumber, receivedAt, auditRecordId,
            ProductionRetryDelays, ct);

    /// <summary>
    /// Public overload with injectable retry delays. Tests pass [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]
    /// to skip real waits while exercising the full retry and SLA logic.
    /// </summary>
    public async Task DispatchConfirmationAsync_WithDelays(
        string phoneNumber,
        string coolTextAccountNumber,
        DateTimeOffset receivedAt,
        long auditRecordId,
        TimeSpan[] retryDelays,
        CancellationToken ct)
    {
        var phoneHash = _hasher.Hash(phoneNumber);
        string messageBody;

        try
        {
            messageBody = await _configRepo.GetRequiredValueAsync("OptOutMessageBody", ct);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "OptOutMessageBody config key is missing or empty. Cannot dispatch confirmation for {PhoneHash}",
                phoneHash);
            await WriteAuditAsync(
                AuditEventType.ConfirmationFailed,
                phoneNumber,
                JsonSerializer.Serialize(new { reason = "OptOutMessageBody config missing", auditRecordId }),
                ct);
            return;
        }

        // Retry loop: attempt 0 (initial) through retryDelays.Length (total = retryDelays.Length + 1 attempts).
        CoolTextSendResult? lastResult = null;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(retryDelays[attempt - 1], ct);

            try
            {
                lastResult = await _coolTextClient.SendSmsAsync(phoneNumber, coolTextAccountNumber, messageBody, ct);

                if (lastResult.Success)
                    break;

                _logger.LogWarning(
                    "Cool Text returned failure on attempt {Attempt}/{Total} for {PhoneHash}: {Error}",
                    attempt + 1, retryDelays.Length + 1, phoneHash, lastResult.ErrorMessage);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Cool Text threw on attempt {Attempt}/{Total} for {PhoneHash}",
                    attempt + 1, retryDelays.Length + 1, phoneHash);
            }
        }

        var dispatchedAt = DateTimeOffset.UtcNow;
        var latencySeconds = (dispatchedAt - receivedAt).TotalSeconds;

        if (lastResult?.Success == true)
        {
            await WriteAuditAsync(
                AuditEventType.ConfirmationDispatched,
                phoneNumber,
                JsonSerializer.Serialize(new
                {
                    providerMessageId = lastResult.MessageId,
                    latencySeconds = Math.Round(latencySeconds, 1),
                    auditRecordId
                }),
                ct);

            if (dispatchedAt - receivedAt > SlaThreshold)
            {
                _logger.LogCritical(
                    "SLA breach: confirmation dispatched {LatencySeconds}s after receipt for {PhoneHash} (threshold {ThresholdSeconds}s)",
                    latencySeconds, phoneHash, SlaThreshold.TotalSeconds);
                await WriteAuditAsync(
                    AuditEventType.SlaBreach,
                    phoneNumber,
                    JsonSerializer.Serialize(new
                    {
                        latencySeconds = Math.Round(latencySeconds, 1),
                        thresholdSeconds = SlaThreshold.TotalSeconds
                    }),
                    ct);
            }
        }
        else
        {
            var reason = lastException?.Message ?? lastResult?.ErrorMessage ?? "Unknown";
            _logger.LogCritical(
                "Confirmation dispatch failed after all retries for {PhoneHash}: {Reason}",
                phoneHash, reason);
            await WriteAuditAsync(
                AuditEventType.ConfirmationFailed,
                phoneNumber,
                JsonSerializer.Serialize(new { reason, auditRecordId }),
                ct);
        }
    }

    private async Task WriteAuditAsync(
        AuditEventType eventType,
        string phoneNumber,
        string details,
        CancellationToken ct)
    {
        _auditRepo.Write(new AuditLog
        {
            EventType = eventType,
            PhoneNumber = phoneNumber,
            OccurredAt = DateTime.UtcNow,
            Details = details
        });
        await _ctx.SaveChangesAsync(ct);
    }
}
