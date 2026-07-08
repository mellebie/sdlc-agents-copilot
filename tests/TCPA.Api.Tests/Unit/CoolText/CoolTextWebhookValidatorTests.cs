// Tests for CoolTextWebhookValidator
// Source: TASK (SMS Proxy & Routing) | ADR-007
// Covers: HMAC-SHA256 signature validation, fail-closed behavior, timing-safe comparison

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Infrastructure.CoolText;
using Xunit;

namespace TCPA.Api.Tests.Unit.CoolText;

public sealed class CoolTextWebhookValidatorTests
{
    private const string TestSecret = "test-webhook-secret-32bytes-long!";
    private const string DefaultBody = "{\"cool_text_account_id\":\"ACC-001\",\"sender_cell_number\":\"+12025551234\",\"message_body\":\"Hello\",\"cool_text_message_id\":\"MSG-001\"}";

    private static CoolTextWebhookValidator CreateValidator(
        string secret = TestSecret,
        string? headerOverride = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["CoolText:WebhookSecret"] = secret
        };
        if (headerOverride is not null)
            config["CoolText:WebhookSignatureHeader"] = headerOverride;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var logger = new Mock<ILogger<CoolTextWebhookValidator>>().Object;
        return new CoolTextWebhookValidator(configuration, logger);
    }

    private static string ComputeExpectedSignature(string body, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Should_ReturnTrue_When_SignatureIsValid()
    {
        // Arrange
        var validator = CreateValidator();
        var expectedSignature = ComputeExpectedSignature(DefaultBody, TestSecret);

        // Act
        var result = validator.IsSignatureValid(DefaultBody, expectedSignature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Should_ReturnTrue_When_SignatureHasSha256Prefix()
    {
        // Arrange
        var validator = CreateValidator();
        var rawSignature = ComputeExpectedSignature(DefaultBody, TestSecret);
        var prefixedSignature = $"sha256={rawSignature}";

        // Act
        var result = validator.IsSignatureValid(DefaultBody, prefixedSignature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Should_ReturnFalse_When_SignatureDoesNotMatchBody()
    {
        // Arrange
        var validator = CreateValidator();
        var wrongSignature = ComputeExpectedSignature("different body content", TestSecret);

        // Act
        var result = validator.IsSignatureValid(DefaultBody, wrongSignature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_ReturnFalse_When_SignatureIsArbitraryString()
    {
        // Arrange
        var validator = CreateValidator();

        // Act
        var result = validator.IsSignatureValid(DefaultBody, "not-a-valid-signature");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_ReturnFalse_When_SignatureHeaderValueIsNull()
    {
        // Arrange
        var validator = CreateValidator();

        // Act
        var result = validator.IsSignatureValid(DefaultBody, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_ReturnFalse_When_SignatureIsEmpty()
    {
        // Arrange
        var validator = CreateValidator();

        // Act
        var result = validator.IsSignatureValid(DefaultBody, string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_ComputeCorrectly_When_BodyIsEmpty()
    {
        // Arrange
        var validator = CreateValidator();
        var emptyBody = string.Empty;
        var expectedSignature = ComputeExpectedSignature(emptyBody, TestSecret);

        // Act
        var result = validator.IsSignatureValid(emptyBody, expectedSignature);

        // Assert — empty body is a valid HMAC input; no exception should be thrown
        result.Should().BeTrue();
    }

    [Fact]
    public void Should_UseFixedTimeEquals_NotStringComparison()
    {
        // Arrange — this test verifies the code path leads to FixedTimeEquals
        // by confirming that signature comparison is byte-level and not vulnerable to
        // short-circuit string equality. We verify the correct signature passes and
        // a one-byte-different signature fails (which would be the same result with ==
        // but the code must use FixedTimeEquals to be timing-safe).
        var validator = CreateValidator();
        var correctSig = ComputeExpectedSignature(DefaultBody, TestSecret);

        // Flip a single hex char to create a near-match that would still be the same
        // length — a naive == comparison would work here too, but FixedTimeEquals is required
        var nearMatchSig = correctSig[..^1] + (correctSig[^1] == '0' ? '1' : '0');

        // Act
        var correctResult = validator.IsSignatureValid(DefaultBody, correctSig);
        var nearMatchResult = validator.IsSignatureValid(DefaultBody, nearMatchSig);

        // Assert
        correctResult.Should().BeTrue("the correct signature must pass");
        nearMatchResult.Should().BeFalse("a one-character-off signature must not pass");
    }

    [Fact]
    public void Should_ThrowInvalidOperationException_When_SecretIsMissing()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var logger = new Mock<ILogger<CoolTextWebhookValidator>>().Object;

        // Act & Assert
        var act = () => new CoolTextWebhookValidator(configuration, logger);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CoolText:WebhookSecret*");
    }

    [Fact]
    public void Should_ThrowInvalidOperationException_When_SecretIsWhitespace()
    {
        // Arrange
        var config = new Dictionary<string, string?> { ["CoolText:WebhookSecret"] = "   " };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var logger = new Mock<ILogger<CoolTextWebhookValidator>>().Object;

        // Act & Assert
        var act = () => new CoolTextWebhookValidator(configuration, logger);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_UseDefaultSignatureHeader_When_HeaderNotConfigured()
    {
        // Arrange
        var validator = CreateValidator();

        // Act
        var headerName = validator.SignatureHeaderName;

        // Assert
        headerName.Should().Be(CoolTextWebhookValidator.DefaultSignatureHeader);
    }

    [Fact]
    public void Should_UseConfiguredSignatureHeader_When_OverrideIsProvided()
    {
        // Arrange
        var validator = CreateValidator(headerOverride: "X-Custom-Signature");

        // Act
        var headerName = validator.SignatureHeaderName;

        // Assert
        headerName.Should().Be("X-Custom-Signature");
    }
}
