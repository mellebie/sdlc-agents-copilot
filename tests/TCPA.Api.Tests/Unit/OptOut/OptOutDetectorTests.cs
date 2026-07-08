// tests/TCPA.Api.Tests/Unit/OptOut/OptOutDetectorTests.cs
// Tests for OptOutDetector — CTIA opt-out keyword detection
// Source: TASK-015 | SPEC-003 | STORY-004
// Business Rules: BR-010 through BR-015
// ACs covered: all keyword detection ACs

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Services.OptOut;
using Xunit;

namespace TCPA.Api.Tests.Unit.OptOut;

/// <summary>
/// Tests for <see cref="OptOutDetector"/>.
/// Verifies: exact keyword matching, case-insensitivity, word-boundary enforcement,
/// multi-word messages, null/empty guard, and no-match path.
/// </summary>
public sealed class OptOutDetectorTests
{
    private readonly Mock<ILogger<OptOutDetector>> _loggerMock;
    private readonly OptOutDetector _sut;

    public OptOutDetectorTests()
    {
        _loggerMock = new Mock<ILogger<OptOutDetector>>();
        _sut = new OptOutDetector(_loggerMock.Object);
    }

    // -----------------------------------------------------------------------
    // Null and empty input guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Should_ReturnNoMatch_When_MessageBodyIsNull()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(null);

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
        result.MatchedKeyword.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnNoMatch_When_MessageBodyIsEmpty()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(string.Empty);

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
        result.MatchedKeyword.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // No-match path
    // -----------------------------------------------------------------------

    [Fact]
    public void Should_ReturnNoMatch_When_MessageContainsNoOptOutKeyword()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("Hello, I would like more information please.");

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
        result.MatchedKeyword.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Exact keyword matches — each of the 7 CTIA-mandated keywords
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("STOP", "STOP")]
    [InlineData("QUIT", "QUIT")]
    [InlineData("END", "END")]
    [InlineData("REVOKE", "REVOKE")]
    [InlineData("OPT-OUT", "OPT-OUT")]
    [InlineData("CANCEL", "CANCEL")]
    [InlineData("UNSUBSCRIBE", "UNSUBSCRIBE")]
    public void Should_DetectKeyword_When_MessageIsExactKeyword(string message, string expectedKeyword)
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(message);

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be(expectedKeyword);
    }

    // -----------------------------------------------------------------------
    // Case insensitivity — each keyword in lowercase and mixed case
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("stop", "STOP")]
    [InlineData("Stop", "STOP")]
    [InlineData("quit", "QUIT")]
    [InlineData("Quit", "QUIT")]
    [InlineData("end", "END")]
    [InlineData("End", "END")]
    [InlineData("revoke", "REVOKE")]
    [InlineData("Revoke", "REVOKE")]
    [InlineData("opt-out", "OPT-OUT")]
    [InlineData("Opt-Out", "OPT-OUT")]
    [InlineData("cancel", "CANCEL")]
    [InlineData("Cancel", "CANCEL")]
    [InlineData("unsubscribe", "UNSUBSCRIBE")]
    [InlineData("Unsubscribe", "UNSUBSCRIBE")]
    public void Should_DetectKeyword_When_KeywordIsMixedCase(string message, string expectedKeyword)
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(message);

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be(expectedKeyword);
    }

    // -----------------------------------------------------------------------
    // Word boundary enforcement — keywords embedded in longer words must NOT match
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("STOPPING")]
    [InlineData("UNSTOP")]
    [InlineData("stopping messages please")]
    [InlineData("QUITTER")]
    [InlineData("ENDING")]
    [InlineData("REVOKED")]
    [InlineData("CANCELLED")]
    [InlineData("UNSUBSCRIBED")]
    public void Should_ReturnNoMatch_When_KeywordIsSubstringOfLongerWord(string message)
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(message);

        // Assert
        result.IsOptOutKeyword.Should().BeFalse(
            because: $"'{message}' contains the keyword only as a substring, not as a complete word");
        result.MatchedKeyword.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnNoMatch_When_MessageContainsStoppingNotStop()
    {
        // Arrange — "STOPPING" contains "STOP" but must not match (BR-013 word-boundary)
        // Act
        KeywordDetectionResult result = _sut.Detect("STOPPING");

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
    }

    [Fact]
    public void Should_ReturnNoMatch_When_MessageContainsUnstopNotStop()
    {
        // Arrange — "UNSTOP" contains "STOP" as a substring but must not match
        // Act
        KeywordDetectionResult result = _sut.Detect("UNSTOP");

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Multi-word messages — keyword appears somewhere in a longer message
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Please STOP sending me texts", "STOP")]
    [InlineData("I want to QUIT receiving these messages", "QUIT")]
    [InlineData("Please END my subscription", "END")]
    [InlineData("I REVOKE my consent", "REVOKE")]
    [InlineData("Please OPT-OUT me from this list", "OPT-OUT")]
    [InlineData("I would like to CANCEL", "CANCEL")]
    [InlineData("Please UNSUBSCRIBE me from all messages", "UNSUBSCRIBE")]
    public void Should_DetectKeyword_When_KeywordAppearsInMultiWordMessage(string message, string expectedKeyword)
    {
        // Act
        KeywordDetectionResult result = _sut.Detect(message);

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be(expectedKeyword);
    }

    [Fact]
    public void Should_DetectKeyword_When_KeywordAtStartOfMultiWordMessage()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("STOP please, I don't want more messages");

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be("STOP");
    }

    [Fact]
    public void Should_DetectKeyword_When_KeywordAtEndOfMultiWordMessage()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("I want to UNSUBSCRIBE");

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be("UNSUBSCRIBE");
    }

    // -----------------------------------------------------------------------
    // OPT-OUT specifics — "OPT" alone must not trigger; full "OPT-OUT" must
    // -----------------------------------------------------------------------

    [Fact]
    public void Should_ReturnNoMatch_When_MessageContainsOPTAloneNotOPTOUT()
    {
        // BR-013: "OPT" alone is not an opt-out keyword; only "OPT-OUT" is
        // Act
        KeywordDetectionResult result = _sut.Detect("OPT");

        // Assert
        result.IsOptOutKeyword.Should().BeFalse();
    }

    [Fact]
    public void Should_DetectOPTOUT_When_MessageContainsFullHyphenatedKeyword()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("OPT-OUT");

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be("OPT-OUT");
    }

    // -----------------------------------------------------------------------
    // Punctuation and whitespace edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Should_DetectKeyword_When_KeywordFollowedByPunctuation()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("STOP.");

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be("STOP");
    }

    [Fact]
    public void Should_DetectKeyword_When_MessageIsKeywordWithLeadingAndTrailingWhitespace()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("  STOP  ");

        // Assert
        result.IsOptOutKeyword.Should().BeTrue();
        result.MatchedKeyword.Should().Be("STOP");
    }

    // -----------------------------------------------------------------------
    // Return value contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Should_ReturnNullMatchedKeyword_When_NoKeywordDetected()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("Just a normal reply message");

        // Assert
        result.MatchedKeyword.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnPopulatedMatchedKeyword_When_KeywordDetected()
    {
        // Act
        KeywordDetectionResult result = _sut.Detect("STOP");

        // Assert
        result.MatchedKeyword.Should().NotBeNullOrEmpty();
        result.IsOptOutKeyword.Should().BeTrue();
    }
}
