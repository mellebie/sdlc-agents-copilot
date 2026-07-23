namespace TCPA.Core.Services;

public record KeywordDetectionResult(bool IsOptOut, string? MatchedKeyword);

public interface IKeywordDetectionService
{
    KeywordDetectionResult Detect(string? messageBody);
}

public class KeywordDetectionService : IKeywordDetectionService
{
    // Federal TCPA mandated opt-out keywords — exact match only (SPEC-002, PD-002)
    private static readonly HashSet<string> OptOutKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STOP", "QUIT", "END", "REVOKE", "OPT-OUT", "CANCEL", "UNSUBSCRIBE"
    };

    public KeywordDetectionResult Detect(string? messageBody)
    {
        if (string.IsNullOrWhiteSpace(messageBody))
            return new KeywordDetectionResult(false, null);

        var trimmed = messageBody.Trim();

        // Full equality only — no substring or prefix matching
        if (OptOutKeywords.TryGetValue(trimmed, out var matched))
            return new KeywordDetectionResult(true, matched);

        return new KeywordDetectionResult(false, null);
    }
}
