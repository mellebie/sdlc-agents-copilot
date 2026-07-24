// Tests: InboundMessageWorker — Kafka message routing, retry/poison-pill, scope-per-message
// Source: Task 6 | TCPA.MessageProcessor inbound plan
// Covers: opt-out routing, general reply routing, account-not-found skip, retry on transient error, poison pill

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
using TCPA.MessageProcessor.Messaging;
using TCPA.MessageProcessor.Services;
using TCPA.MessageProcessor.Workers;

namespace TCPA.MessageProcessor.Tests.Workers;

public class InboundMessageWorkerTests
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IKeywordDetectionService _keywordDetector;
    private readonly IPhoneNumberHasher _hasher;
    private readonly IOptOutProcessingService _optOutService;
    private readonly IConfirmationDispatchService _confirmationService;
    private readonly IReplyForwardingService _replyService;
    private readonly ICoolTextAccountRepository _accountRepo;
    private readonly IServiceScopeFactory _scopeFactory;

    public InboundMessageWorkerTests()
    {
        _consumer = Substitute.For<IConsumer<string, string>>();
        _keywordDetector = Substitute.For<IKeywordDetectionService>();
        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);
        _optOutService = Substitute.For<IOptOutProcessingService>();
        _confirmationService = Substitute.For<IConfirmationDispatchService>();
        _replyService = Substitute.For<IReplyForwardingService>();
        _accountRepo = Substitute.For<ICoolTextAccountRepository>();

        var scope = Substitute.For<IServiceScope>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);

        var provider = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(provider);
        provider.GetService(typeof(IOptOutProcessingService)).Returns(_optOutService);
        provider.GetService(typeof(IConfirmationDispatchService)).Returns(_confirmationService);
        provider.GetService(typeof(IReplyForwardingService)).Returns(_replyService);
        provider.GetService(typeof(ICoolTextAccountRepository)).Returns(_accountRepo);
    }

    private InboundMessageWorker BuildWorker()
        => new InboundMessageWorker(
            _consumer, _keywordDetector, _hasher, _scopeFactory,
            Substitute.For<ILogger<InboundMessageWorker>>());

    private static ConsumeResult<string, string> MakeConsumeResult(InboundMessageEvent @event) =>
        new()
        {
            Message = new Message<string, string>
            {
                Key = @event.From,
                Value = JsonSerializer.Serialize(@event)
            },
            TopicPartitionOffset = new TopicPartitionOffset("inbound-messages", 0, 1)
        };

    private static InboundMessageEvent StopEvent() =>
        new("int-1", "msg-1", "+12025551234", "CT-001", "STOP", "CoolText", "CT-001", "app1", DateTimeOffset.UtcNow);

    private static InboundMessageEvent ReplyEvent() =>
        new("int-2", "msg-2", "+12025559876", "CT-001", "Hello I need help", "CoolText", "CT-001", "app1", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ProcessMessageAsync_WhenOptOutKeyword_CallsOptOutAndConfirmation()
    {
        // Arrange
        var worker = BuildWorker();
        var @event = StopEvent();
        _keywordDetector.Detect("STOP").Returns(new KeywordDetectionResult(true, "STOP"));
        _optOutService.ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>())
            .Returns(new OptOutResult(true, 42L));

        // Act
        await worker.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert
        await _optOutService.Received(1).ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>());
        await _confirmationService.Received(1).DispatchConfirmationAsync(
            "+12025551234", "CT-001", @event.Timestamp, 42L, Arg.Any<CancellationToken>());
        await _replyService.DidNotReceive().ForwardReplyAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenGeneralReply_CallsForwardWithCallbackUrl()
    {
        // Arrange
        var worker = BuildWorker();
        var @event = ReplyEvent();
        _keywordDetector.Detect(Arg.Any<string>()).Returns(new KeywordDetectionResult(false, null));
        _accountRepo.GetByAccountNumberAsync("CT-001", Arg.Any<CancellationToken>())
            .Returns(new CoolTextAccount
            {
                AccountNumber = "CT-001",
                CallbackUrl = "https://app.example.com/sms",
                IsActive = true,
                ApplicationId = "app1",
                ApplicationName = "Test App",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        // Act
        await worker.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert
        await _replyService.Received(1).ForwardReplyAsync(
            Arg.Any<InboundMessageEvent>(), "https://app.example.com/sms", Arg.Any<CancellationToken>());
        await _optOutService.DidNotReceive().ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenAccountNotFound_SkipsForwardAndLogsWarning()
    {
        // Arrange
        var worker = BuildWorker();
        _keywordDetector.Detect(Arg.Any<string>()).Returns(new KeywordDetectionResult(false, null));
        _accountRepo.GetByAccountNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CoolTextAccount?)null);

        // Act
        var act = () => worker.ProcessMessageAsync_ForTesting(MakeConsumeResult(ReplyEvent()), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await _replyService.DidNotReceive().ForwardReplyAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenOptOutServiceThrows_SecondAttemptIsRetried()
    {
        // Arrange
        var worker = BuildWorker();
        var @event = StopEvent();
        _keywordDetector.Detect(Arg.Any<string>()).Returns(new KeywordDetectionResult(true, "STOP"));
        int callCount = 0;
        _optOutService.ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1) throw new Exception("transient DB error");
                return Task.FromResult(new OptOutResult(true, 42L));
            });

        // Act — process with retry
        await worker.ProcessMessageAsync_ForTesting(MakeConsumeResult(@event), CancellationToken.None);

        // Assert: called twice (1 failure + 1 success)
        await _optOutService.Received(2).ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenBothAttemptsThrow_CompletesWithoutThrowingToCaller()
    {
        // Arrange
        var worker = BuildWorker();
        _keywordDetector.Detect(Arg.Any<string>()).Returns(new KeywordDetectionResult(true, "STOP"));
        _optOutService.ProcessOptOutAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("persistent DB failure"));

        // Act — poison pill path must not propagate
        var act = () => worker.ProcessMessageAsync_ForTesting(MakeConsumeResult(StopEvent()), CancellationToken.None);

        // Assert: no throw — offset will be committed by worker, partition unblocked
        await act.Should().NotThrowAsync();
    }
}
