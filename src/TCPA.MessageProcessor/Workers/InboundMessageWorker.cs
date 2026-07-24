using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCPA.Core.Interfaces;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Messaging;
using TCPA.MessageProcessor.Services;

namespace TCPA.MessageProcessor.Workers;

/// <summary>
/// Kafka consumer BackgroundService — subscribes to the inbound-messages topic,
/// deserializes <see cref="InboundMessageEvent"/> payloads, routes each message to
/// <see cref="IOptOutProcessingService"/> or <see cref="IReplyForwardingService"/>
/// based on keyword detection, and commits the Kafka offset after processing.
///
/// Retry policy: each message is attempted up to 2 times. If both attempts fail the
/// offset is committed and a Critical log entry is written (poison-pill drain pattern)
/// so the partition is never permanently blocked.
///
/// Scope-per-message: scoped services (repositories, processing services) are resolved
/// from a fresh <see cref="IServiceScope"/> created by <see cref="IServiceScopeFactory"/>
/// for each message-processing attempt. This ensures EF Core DbContext instances are not
/// reused across messages.
/// </summary>
public class InboundMessageWorker : BackgroundService
{
    private const string TopicName = "inbound-messages";
    private const int MaxProcessingAttempts = 2;

    private readonly IConsumer<string, string> _consumer;
    private readonly IKeywordDetectionService _keywordDetector;
    private readonly IPhoneNumberHasher _hasher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboundMessageWorker> _logger;

    /// <summary>Initializes a new instance of <see cref="InboundMessageWorker"/>.</summary>
    public InboundMessageWorker(
        IConsumer<string, string> consumer,
        IKeywordDetectionService keywordDetector,
        IPhoneNumberHasher hasher,
        IServiceScopeFactory scopeFactory,
        ILogger<InboundMessageWorker> logger)
    {
        _consumer = consumer;
        _keywordDetector = keywordDetector;
        _hasher = hasher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(TopicName);
        _logger.LogInformation("InboundMessageWorker subscribed to topic {TopicName}", TopicName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> consumeResult;
                try
                {
                    consumeResult = _consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (consumeResult?.Message is null)
                    continue;

                await ProcessMessageAsync_ForTesting(consumeResult, stoppingToken);
                _consumer.Commit(consumeResult);
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("InboundMessageWorker stopped and consumer closed.");
        }
    }

    /// <summary>
    /// Processes a single Kafka message with up to <see cref="MaxProcessingAttempts"/> attempts.
    /// On all-attempts-failed (poison pill), logs Critical and returns without throwing so the
    /// caller can commit the offset and unblock the partition.
    ///
    /// Exposed as <c>internal</c> to enable direct unit testing without running the Kafka consume loop.
    /// </summary>
    internal async Task ProcessMessageAsync_ForTesting(
        ConsumeResult<string, string> consumeResult, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxProcessingAttempts; attempt++)
        {
            try
            {
                await ProcessMessageCoreAsync(consumeResult, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Message processing attempt {Attempt}/{MaxAttempts} failed. Partition={Partition} Offset={Offset}",
                    attempt, MaxProcessingAttempts,
                    consumeResult.Partition.Value, consumeResult.Offset.Value);

                if (attempt == MaxProcessingAttempts)
                {
                    _logger.LogCritical(
                        "Poison pill: all {MaxAttempts} attempts failed for Partition={Partition} Offset={Offset}. Committing offset to unblock partition.",
                        MaxProcessingAttempts,
                        consumeResult.Partition.Value, consumeResult.Offset.Value);
                    // Do NOT rethrow — offset is committed by the caller in ExecuteAsync
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }
    }

    /// <summary>Deserializes the message and dispatches it to the correct processing path.</summary>
    private async Task ProcessMessageCoreAsync(
        ConsumeResult<string, string> consumeResult, CancellationToken ct)
    {
        InboundMessageEvent @event;
        try
        {
            @event = JsonSerializer.Deserialize<InboundMessageEvent>(consumeResult.Message.Value)
                ?? throw new InvalidOperationException("Deserialized event was null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize message at Partition={Partition} Offset={Offset}",
                consumeResult.Partition.Value, consumeResult.Offset.Value);
            return; // Malformed payload — skip without retry; offset committed by caller
        }

        // A fresh scope per attempt ensures scoped services (DbContext, repositories) are never
        // reused across messages or across retry attempts.
        await using var scope = _scopeFactory.CreateAsyncScope();

        var detectionResult = _keywordDetector.Detect(@event.Body);

        if (detectionResult.IsOptOut)
        {
            await ProcessOptOutMessageAsync(@event, scope.ServiceProvider, ct);
        }
        else
        {
            await ProcessGeneralReplyAsync(@event, scope.ServiceProvider, ct);
        }
    }

    /// <summary>
    /// Handles opt-out keyword messages:
    /// 1. Writes opt-out status + audit record atomically (BR-010).
    /// 2. Dispatches confirmation SMS after status is written (BR-009).
    /// </summary>
    private async Task ProcessOptOutMessageAsync(
        InboundMessageEvent @event, IServiceProvider services, CancellationToken ct)
    {
        var optOutService = services.GetRequiredService<IOptOutProcessingService>();
        var confirmationService = services.GetRequiredService<IConfirmationDispatchService>();

        var result = await optOutService.ProcessOptOutAsync(@event, ct);

        _logger.LogInformation(
            "Opt-out processed. IsNew={IsNew} PhoneHash={PhoneHash} MessageId={MessageId}",
            result.IsNew, _hasher.Hash(@event.From), @event.MessageId);

        // BR-009: confirmation is dispatched only after opt-out status is committed
        await confirmationService.DispatchConfirmationAsync(
            @event.From, @event.CoolTextAccountNumber, @event.Timestamp, result.AuditRecordId, ct);
    }

    /// <summary>
    /// Handles general (non-opt-out) inbound replies:
    /// 1. Looks up the Cool Text account to get the callback URL (BR-016).
    /// 2. Forwards the message body to that URL (BR-015, BR-017).
    /// If the account is inactive or not found, logs a warning and skips forwarding.
    /// </summary>
    private async Task ProcessGeneralReplyAsync(
        InboundMessageEvent @event, IServiceProvider services, CancellationToken ct)
    {
        var accountRepo = services.GetRequiredService<ICoolTextAccountRepository>();
        var replyService = services.GetRequiredService<IReplyForwardingService>();

        var account = await accountRepo.GetByAccountNumberAsync(@event.CoolTextAccountNumber, ct);
        if (account is null || !account.IsActive)
        {
            _logger.LogWarning(
                "No active Cool Text account found for AccountNumber={AccountNumber}. MessageId={MessageId} PhoneHash={PhoneHash}",
                @event.CoolTextAccountNumber, @event.MessageId, _hasher.Hash(@event.From));
            return;
        }

        await replyService.ForwardReplyAsync(@event, account.CallbackUrl, ct);
    }
}
