using FluentAssertions;
using IntakeApi.Contracts;
using IntakeApi.Services;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class InboundMessageRequestValidatorTests
{
    [Fact]
    public void Validate_WhenRequestIsNull_ReturnsFailure()
    {
        var validator = new InboundMessageRequestValidator();

        var result = validator.Validate(null);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle(f => f.Field == "request" && f.Code == "INVALID_INPUT");
    }

    [Fact]
    public void Validate_WhenMessageTooLong_ReturnsFailure()
    {
        var validator = new InboundMessageRequestValidator();
        var request = new InboundMessageRequest
        {
            EventId = "evt-001",
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "+14045550100",
            SourceLdc = SourceLdc.Vng,
            SourceApplication = SourceApplication.BizTalk,
            CoolTextAccountId = "acct-001",
            MessageText = new string('A', 1601)
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Field == "messageText" && f.Message.Contains("1600"));
    }
}
