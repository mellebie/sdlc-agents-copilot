using System.Text.RegularExpressions;

namespace IntentClassifier.Services;

public interface IIntentClassificationService
{
    IntentClassificationResult Classify(string? messageText);
    IntentClassificationRecord CreateRecord(string eventId, string? messageText);
}

public sealed class IntentClassificationService : IIntentClassificationService
{
    private static readonly HashSet<string> StopKeywords =
    [
        "STOP",
        "QUIT",
        "END",
        "REVOKE",
        "OPT-OUT",
        "CANCEL",
        "UNSUBSCRIBE"
    ];

    public IntentClassificationResult Classify(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return new IntentClassificationResult(false, NormalizedIntent.Invalid, null, "MALFORMED_PAYLOAD");
        }

        var tokens = Regex.Split(messageText.Trim().ToUpperInvariant(), @"[^A-Z0-9-]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        foreach (var token in tokens)
        {
            if (StopKeywords.Contains(token))
            {
                return new IntentClassificationResult(true, NormalizedIntent.Stop, token, null);
            }
        }

        if (tokens.Any(token => token == "HELP"))
        {
            return new IntentClassificationResult(true, NormalizedIntent.Help, "HELP", null);
        }

        return new IntentClassificationResult(true, NormalizedIntent.Other, null, null);
    }

    public IntentClassificationRecord CreateRecord(string eventId, string? messageText)
    {
        var result = Classify(messageText);
        return new IntentClassificationRecord(
            eventId,
            messageText,
            result.Intent,
            result.MatchedKeyword,
            result.Success,
            result.FailureCode,
            DateTimeOffset.UtcNow);
    }
}

public enum NormalizedIntent
{
    Invalid = 0,
    Stop = 1,
    Help = 2,
    Other = 3
}

public readonly record struct IntentClassificationResult(
    bool Success,
    NormalizedIntent Intent,
    string? MatchedKeyword,
    string? FailureCode);

public readonly record struct IntentClassificationRecord(
    string EventId,
    string? OriginalMessageText,
    NormalizedIntent Intent,
    string? MatchedKeyword,
    bool Success,
    string? FailureCode,
    DateTimeOffset ClassifiedAtUtc);
