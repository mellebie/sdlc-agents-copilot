using FluentAssertions;
using IntentClassifier.Services;
using Xunit;

namespace IntakeApi.Tests.Services;

public sealed class IntentClassificationServiceTests
{
    private readonly IntentClassificationService _service = new();

    [Theory]
    [InlineData("STOP")]
    [InlineData("QUIT")]
    [InlineData("END")]
    [InlineData("REVOKE")]
    [InlineData("OPT-OUT")]
    [InlineData("CANCEL")]
    [InlineData("UNSUBSCRIBE")]
    public void Classify_AllStopKeywords_MapToStop(string keyword)
    {
        var result = _service.Classify(keyword);

        result.Success.Should().BeTrue();
        result.Intent.Should().Be(NormalizedIntent.Stop);
        result.MatchedKeyword.Should().Be(keyword);
        result.FailureCode.Should().BeNull();
    }

    [Fact]
    public void Classify_MixedCaseWithPunctuation_MapsToStop()
    {
        var result = _service.Classify("Please, sToP!!!");

        result.Success.Should().BeTrue();
        result.Intent.Should().Be(NormalizedIntent.Stop);
        result.MatchedKeyword.Should().Be("STOP");
    }

    [Fact]
    public void Classify_MalformedPayload_ReturnsFailure()
    {
        var result = _service.Classify("   ");

        result.Success.Should().BeFalse();
        result.Intent.Should().Be(NormalizedIntent.Invalid);
        result.FailureCode.Should().Be("MALFORMED_PAYLOAD");
    }

    [Fact]
    public void CreateRecord_PersistsMatchedKeywordForAudit()
    {
        var record = _service.CreateRecord("evt-123", "opt-out now");

        record.EventId.Should().Be("evt-123");
        record.Success.Should().BeTrue();
        record.Intent.Should().Be(NormalizedIntent.Stop);
        record.MatchedKeyword.Should().Be("OPT-OUT");
    }
}
