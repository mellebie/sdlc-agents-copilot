using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.OutboundDispatcher.Messaging;
using TCPA.OutboundDispatcher.Services;

namespace TCPA.OutboundDispatcher.Workers;

/// <summary>
/// Kafka consumer <see cref="BackgroundService"/> — subscribes to the <c>outbound-messages</c>
/// topic, deserializes <see cref="OutboundMessageEvent"/> payloads, enforces idempotency,
/// routes each message through the outbound gate and send pipeline, and commits the Kafka
/// offset after processing.
///
/// Processing lifecycle per message:
/// 1. Idempotency check — if <see cref="IProcessedMessageRepository.FindAsync"/> returns a record,
///    the message was already processed (Kafka at-least-once redelivery). Skip silently.
/// 2. Gate evaluation — <see cref="IOutboundGateService"/> checks opt-out status and TCPA quiet hours.
///    If suppressed, writes <c>OutboundSuppressed</c> audit, records processed, returns.
/// 3. Send — <see cref="IOutboundSendService"/> calls Cool Text API with retry.
///    Writes <c>OutboundDelivered</c> or <c>OutboundFailed</c> audit.
/// 4. Record processed — <see cref="IProcessedMessageRepository.AddAsync"/> guards future replays.
///
/// Retry policy: each message is attempted up to 2 times. If both attempts fail the offset is
/// committed and a Critical log entry is written (poison-pill drain pattern) so the partition
/// is never permanently blocked.
///
/// Scope-per-message: scoped services are resolved from a fresh <see cref="IServiceScope"/>
/// created by <see cref="IServiceScopeFactory"/> for each message-processing attempt.
/// This ensures EF Core DbContext instances are not reused across messages.
/// </summary>
public class OutboundMessageWorker : BackgroundService
{
    private const string TopicName = "outbound-messages";
    private const string EndpointKey = "outbound-dispatcher";
    private const int MaxProcessingAttempts = 2;

    private readonly IConsumer<string, string> _consumer;
    private readonly IPhoneNumberHasher _hasher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboundMessageWorker> _logger;

    /// <summary>Initializes a new instance of <see cref="OutboundMessageWorker"/>.</summary>
    public OutboundMessageWorker(
        IConsumer<string, string> consumer,
        IPhoneNumberHasher hasher,
        IServiceScopeFactory scopeFactory,
        ILogger<OutboundMessageWorker> logger)
    {
        _consumer = consumer;
        _hasher = hasher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(TopicName);
        _logger.LogInformation("OutboundMessageWorker subscribed to topic {TopicName}", TopicName);

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
            _logger.LogInformation("OutboundMessageWorker stopped and consumer closed.");
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

    /// <summary>
    /// Deserializes the event, checks idempotency, evaluates the gate, and dispatches to send.
    /// </summary>
    private async Task ProcessMessageCoreAsync(
        ConsumeResult<string, string> consumeResult, CancellationToken ct)
    {
        OutboundMessageEvent @event;
        try
        {
            @event = JsonSerializer.Deserialize<OutboundMessageEvent>(consumeResult.Message.Value)
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
        var services = scope.ServiceProvider;

        // Step 1: Idempotency guard — skip Kafka at-least-once redeliveries
        var processedRepo = services.GetRequiredService<IProcessedMessageRepository>();
        var existing = await processedRepo.FindAsync(@event.MessageId, EndpointKey, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate Kafka delivery skipped. MessageId={MessageId} PhoneHash={PhoneHash}",
                @event.MessageId, _hasher.Hash(@event.ToNumber));
            return;
        }

        // Step 2: Gate evaluation — opt-out + quiet hours
        var gateService = services.GetRequiredService<IOutboundGateService>();
        var gateResult = await gateService.EvaluateAsync(@event, ct);

        if (!gateResult.IsAllowed)
        {
            // Gate already wrote OutboundSuppressed audit — record idempotency entry and stop
            await processedRepo.AddAsync(new ProcessedMessage
            {
                MessageId = @event.MessageId,
                Endpoint = EndpointKey,
                ResponseStatus = "suppressed",
                ProcessedAt = DateTime.UtcNow
            }, ct);
            return;
        }

        // Step 3: Send via Cool Text
        var sendService = services.GetRequiredService<IOutboundSendService>();
        await sendService.SendAsync(@event, ct);

        // Step 4: Record as processed (outcome is final — delivered or failed)
        await processedRepo.AddAsync(new ProcessedMessage
        {
            MessageId = @event.MessageId,
            Endpoint = EndpointKey,
            ResponseStatus = "delivered", // OutboundFailed is still a final outcome; guard future replays
            ProcessedAt = DateTime.UtcNow
        }, ct);
    }
}
