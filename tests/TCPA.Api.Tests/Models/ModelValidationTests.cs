using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using TCPA.Api.Models;
using Xunit;

namespace TCPA.Api.Tests.Models;

public class ModelValidationTests
{
    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(model);
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void InboundWebhookRequest_Valid_NoErrors()
    {
        var req = new InboundWebhookRequest
        {
            From = "+14045551234",
            To = "+18005559876",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = "ct-abc-123",
            Timestamp = DateTimeOffset.UtcNow
        };
        Validate(req).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-e164")]
    [InlineData("14045551234")]      // missing leading +
    public void InboundWebhookRequest_InvalidFrom_HasError(string? from)
    {
        var req = new InboundWebhookRequest
        {
            From = from!,
            To = "+18005559876",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = "id",
            Timestamp = DateTimeOffset.UtcNow
        };
        Validate(req).Should().NotBeEmpty();
    }

    [Fact]
    public void OutboundMessageRequest_Valid_NoErrors()
    {
        var req = new OutboundMessageRequest
        {
            ToNumber = "+14045551234",
            Body = "Your bill is due.",
            CoolTextAccountNumber = "CT-001",
            ApplicationId = "biztalk"
        };
        Validate(req).Should().BeEmpty();
    }

    [Fact]
    public void OutboundMessageRequest_BodyOver160Chars_HasError()
    {
        var req = new OutboundMessageRequest
        {
            ToNumber = "+14045551234",
            Body = new string('x', 161),
            CoolTextAccountNumber = "CT-001",
            ApplicationId = "biztalk"
        };
        Validate(req).Should().NotBeEmpty();
    }

    [Fact]
    public void ReOptInRequest_Valid_NoErrors()
    {
        var req = new ReOptInRequest
        {
            PhoneNumber = "+14045551234",
            Reason = "Customer called Help Desk.",
            AgentId = "hdagent-jsmith"
        };
        Validate(req).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ReOptInRequest_MissingReason_HasError(string? reason)
    {
        var req = new ReOptInRequest
        {
            PhoneNumber = "+14045551234",
            Reason = reason!,
            AgentId = "hdagent-jsmith"
        };
        Validate(req).Should().NotBeEmpty();
    }

    [Fact]
    public void ReOptInRequest_ReasonOver500Chars_HasError()
    {
        var req = new ReOptInRequest
        {
            PhoneNumber = "+14045551234",
            Reason = new string('x', 501),
            AgentId = "hdagent-jsmith"
        };
        Validate(req).Should().NotBeEmpty();
    }
}
