// Tests: OutboundMessageWorker — gate-suppress path, send path, duplicate skip, retry, poison pill
// Source: Task 5 | SPEC-006 + SPEC-007 | InternalsVisibleTo: TCPA.OutboundDispatcher

using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FluentAssertions;
using Xunit;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.OutboundDispatcher.Messaging;
using TCPA.OutboundDispatcher.Services;
using TCPA.OutboundDispatcher.Workers;

namespace TCPA.OutboundDispatcher.Tests.Workers;

public class OutboundMessageWorkerTests
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IPhoneNumberHasher _hasher;
    private readonly IOutboundGateService _gateService;
    private readonly IOutboundSendService _sendService;
    private readonly IProcessedMessageRepository _processedRepo;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboundMessageWorkerTests()
    {
        _consumer = Substitute.For<IConsumer<string, string>>();
        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);
        _gateService = Substitute.For<IOutboundGateService>();
        _sendService = Substitute.For<IOutboundSendService>();
        _processedRepo = Substitute.For<IProcessedMessageRepository>();

        var scope = Substitute.For<IServiceScope>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);

        var provider = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(provider);
        provider.GetService(typeof(IOutboundGateService)).Returns(_gateService);
        provider.GetService(typeof(IOutboundSendService)).Returns(_sendService);
        provider.GetService(typeof(IProcessedMessageRepository)).Returns(_processedRepo);
    }

    private OutboundMessageWorker BuildWorker()
        => new OutboundMessageWorker(
            _consumer, _hasher, _scopeFactory,
            Substitute.For<ILogger<OutboundMessageWorker>>());

    private static ConsumeResult<string, string> MakeConsumeResult(OutboundMessageEvent @event) =>
        new()
        {
            Message = new Message<string, string>
            {
                Key = @event.ToNumber,
                Value = JsonSerializer.Serialize(@event)
            },
            TopicPartitionOffset = new TopicPartitionOffset("outbound-messages", 0, 1)
        };

    private static OutboundMessageEvent MakeEvent(string messageId = "msg-worker-001") =>
        new OutboundMessageEvent(
            MessageId: messageId,
            ToNumber: "+12025551234",
            Body: "Hello from outbound worker test",
            CoolTextAccountNumber: "CT-ACCT-001",
            ApplicationId: "app-test",
            CorrelationId: null,
            QueuedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task ProcessMessageAsync_WhenGateAllows_CallsSendServiceAndRecordsProcessed()
    {
        // Arrange
        var @event = MakeEvent();
        _processedRepo.FindAsync(@event.MessageId, "outbound-dispatcher", Arg.Any<CancellationToken>())
            .Returns((ProcessedMessage?)null);
        _gateService.EvaluateAsync(@event, Arg.Any<CancellationToken>())
            .Returns(new GateResult(true, null));
        var sut = BuildWorker();

        // Act
        await sut.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert
        await _gateService.Received(1).EvaluateAsync(@event, Arg.Any<CancellationToken>());
        await _sendService.Received(1).SendAsync(@event, Arg.Any<CancellationToken>());
        await _processedRepo.Received(1).AddAsync(
            Arg.Is<ProcessedMessage>(m => m.MessageId == @event.MessageId && m.Endpoint == "outbound-dispatcher"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenGateSuppresses_DoesNotCallSendServiceButRecordsProcessed()
    {
        // Arrange
        var @event = MakeEvent();
        _processedRepo.FindAsync(@event.MessageId, "outbound-dispatcher", Arg.Any<CancellationToken>())
            .Returns((ProcessedMessage?)null);
        _gateService.EvaluateAsync(@event, Arg.Any<CancellationToken>())
            .Returns(new GateResult(false, "opt_out"));
        var sut = BuildWorker();

        // Act
        await sut.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert
        await _sendService.DidNotReceive().SendAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
        await _processedRepo.Received(1).AddAsync(
            Arg.Is<ProcessedMessage>(m =>
                m.MessageId == @event.MessageId &&
                m.ResponseStatus == "suppressed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenMessageAlreadyProcessed_SkipsGateAndSend()
    {
        // Arrange: simulate Kafka at-least-once redelivery
        var @event = MakeEvent();
        _processedRepo.FindAsync(@event.MessageId, "outbound-dispatcher", Arg.Any<CancellationToken>())
            .Returns(new ProcessedMessage
            {
                MessageId = @event.MessageId,
                Endpoint = "outbound-dispatcher",
                ResponseStatus = "delivered",
                ProcessedAt = DateTime.UtcNow.AddMinutes(-5)
            });
        var sut = BuildWorker();

        // Act
        await sut.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert — no gate, no send, no second ProcessedMessage write
        await _gateService.DidNotReceive().EvaluateAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
        await _sendService.DidNotReceive().SendAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
        await _processedRepo.DidNotReceive().AddAsync(Arg.Any<ProcessedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenGateThrows_RetriesAndEventuallySucceeds()
    {
        // Arrange: first call throws, second succeeds
        var @event = MakeEvent();
        _processedRepo.FindAsync(@event.MessageId, "outbound-dispatcher", Arg.Any<CancellationToken>())
            .Returns((ProcessedMessage?)null);
        _gateService.EvaluateAsync(@event, Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("Transient DB error"),
                _ => Task.FromResult(new GateResult(true, null)));
        _sendService.SendAsync(@event, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sut = BuildWorker();

        // Act
        await sut.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert: gate called twice (second attempt succeeds), send called once
        await _gateService.Received(2).EvaluateAsync(@event, Arg.Any<CancellationToken>());
        await _sendService.Received(1).SendAsync(@event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenBothAttemptsFail_LogsCriticalAndDoesNotThrow()
    {
        // Arrange: poison pill — both attempts throw
        var @event = MakeEvent();
        _processedRepo.FindAsync(@event.MessageId, "outbound-dispatcher", Arg.Any<CancellationToken>())
            .Returns((ProcessedMessage?)null);
        _gateService.EvaluateAsync(@event, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Persistent failure"));
        var sut = BuildWorker();

        // Act — must not throw (poison pill drain pattern)
        Func<Task> act = () => sut.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Assert: gate called twice (both attempts), send never called
        await _gateService.Received(2).EvaluateAsync(@event, Arg.Any<CancellationToken>());
        await _sendService.DidNotReceive().SendAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenMessageJsonIsMalformed_SkipsWithoutRetry()
    {
        // Arrange: corrupt Kafka payload
        var corruptResult = new ConsumeResult<string, string>
        {
            Message = new Message<string, string>
            {
                Key = "key",
                Value = "{ this is not valid json }"
            },
            TopicPartitionOffset = new TopicPartitionOffset("outbound-messages", 0, 99)
        };
        var sut = BuildWorker();

        // Act
        Func<Task> act = () => sut.ProcessMessageAsync_ForTesting(corruptResult, CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Assert: gate and send never called
        await _gateService.DidNotReceive().EvaluateAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
        await _sendService.DidNotReceive().SendAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }
}
