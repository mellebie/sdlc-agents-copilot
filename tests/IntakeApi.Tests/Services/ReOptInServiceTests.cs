using ConsentService.Models;
using ConsentService.Repositories;
using ConsentService.Security;
using ConsentService.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class ReOptInServiceTests
{
    [Fact]
    public async Task ProcessAsync_WithValidChannelAndProof_UpdatesToOptIn()
    {
        var stateRepo = new InMemoryConsentStateRepository();
        await stateRepo.SetStatusAsync("+14045550170", ConsentStatus.OptOut);

        var service = new ReOptInService(
            stateRepo,
            new ReOptInAuthorizationPolicy(),
            new ReplayProtectionService(),
            Substitute.For<IReOptInSecurityEventPublisher>());

        var result = await service.ProcessAsync(new ReOptInTransitionRequest(
            "req-1",
            "+14045550170",
            ReOptInChannel.Form,
            DateTimeOffset.UtcNow,
            "proof",
            "nonce-a"));

        result.Success.Should().BeTrue();
        result.UpdateResult.Should().Be("UPDATED");
        result.UpdatedStatus.Should().Be(ConsentStatus.OptIn);
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidChannel_IsRejected()
    {
        var service = new ReOptInService(
            new InMemoryConsentStateRepository(),
            new ReOptInAuthorizationPolicy(),
            new ReplayProtectionService(),
            Substitute.For<IReOptInSecurityEventPublisher>());

        var result = await service.ProcessAsync(new ReOptInTransitionRequest(
            "req-2",
            "+14045550171",
            ReOptInChannel.Unknown,
            DateTimeOffset.UtcNow,
            "proof",
            "nonce-b"));

        result.Success.Should().BeFalse();
        result.Code.Should().Be("INVALID_REOPTIN_CHANNEL");
    }

    [Fact]
    public async Task ProcessAsync_ReplayAttempt_IsRejectedAndLogged()
    {
        var publisher = Substitute.For<IReOptInSecurityEventPublisher>();
        var now = DateTimeOffset.UtcNow;
        var replay = new ReplayProtectionService();
        var service = new ReOptInService(
            new InMemoryConsentStateRepository(),
            new ReOptInAuthorizationPolicy(),
            replay,
            publisher);

        var first = await service.ProcessAsync(new ReOptInTransitionRequest("req-3", "+14045550172", ReOptInChannel.SmsResponse, now, "proof", "nonce-c"));
        var second = await service.ProcessAsync(new ReOptInTransitionRequest("req-3", "+14045550172", ReOptInChannel.SmsResponse, now.AddMinutes(1), "proof", "nonce-c"));

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.Code.Should().Be("REPLAY_DETECTED");
        await publisher.Received(1).PublishAsync("req-3", "REPLAY_DETECTED", Arg.Any<CancellationToken>());
    }
}
