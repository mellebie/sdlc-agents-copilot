using FluentAssertions;
using TCPA.Core.Services;

namespace TCPA.Core.Tests.Services;

public class KeywordDetectionServiceTests
{
    private readonly KeywordDetectionService _sut = new();

    // --- Exact matches (must detect) ---
    [Theory]
    [InlineData("STOP")]
    [InlineData("QUIT")]
    [InlineData("END")]
    [InlineData("REVOKE")]
    [InlineData("OPT-OUT")]
    [InlineData("CANCEL")]
    [InlineData("UNSUBSCRIBE")]
    public void Detect_ExactKeyword_IsOptOutTrue(string keyword)
    {
        var result = _sut.Detect(keyword);
        result.IsOptOut.Should().BeTrue();
        result.MatchedKeyword.Should().Be(keyword.ToUpperInvariant());
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("Stop")]
    [InlineData("STOP")]
    [InlineData("sTOp")]
    public void Detect_CaseVariants_IsOptOutTrue(string input)
        => _sut.Detect(input).IsOptOut.Should().BeTrue();

    [Theory]
    [InlineData("  STOP  ")]
    [InlineData("\tSTOP\t")]
    [InlineData(" stop ")]
    public void Detect_WhitespacePaddedKeyword_IsOptOutTrue(string input)
        => _sut.Detect(input).IsOptOut.Should().BeTrue();

    // --- Non-matches (must NOT detect) ---
    [Theory]
    [InlineData("Please STOP texting")]
    [InlineData("STOPNOW")]
    [InlineData("STOP NOW")]
    [InlineData("I want to STOP")]
    [InlineData("opt out")]           // space instead of hyphen
    [InlineData("OPT OUT")]           // space instead of hyphen
    [InlineData("CANCELLATION")]
    [InlineData("UNSUBSCRIBED")]
    [InlineData("ENDING")]
    [InlineData("QUIT IT")]
    public void Detect_NonExactMatch_IsOptOutFalse(string input)
        => _sut.Detect(input).IsOptOut.Should().BeFalse();

    [Fact]
    public void Detect_EmptyString_IsOptOutFalse()
        => _sut.Detect("").IsOptOut.Should().BeFalse();

    [Fact]
    public void Detect_Null_IsOptOutFalse()
        => _sut.Detect(null).IsOptOut.Should().BeFalse();

    [Fact]
    public void Detect_WhitespaceOnly_IsOptOutFalse()
        => _sut.Detect("   ").IsOptOut.Should().BeFalse();
}
