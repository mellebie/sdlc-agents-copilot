// src/TCPA.Api/Services/OptOut/IOptOutDetector.cs
// TCPA Compliance Engine — Opt-Out Keyword Detection Interface
// Source: TASK-015 | SPEC-003 | STORY-004

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Result of an opt-out keyword detection scan against an inbound SMS body.
/// </summary>
public sealed record KeywordDetectionResult
{
    /// <summary>
    /// <c>true</c> if any of the seven TCPA-mandated opt-out keywords was
    /// detected as a complete word (word-boundary match) in the message body.
    /// </summary>
    public bool IsOptOutKeyword { get; init; }

    /// <summary>
    /// The matched keyword in its normalized uppercase form (e.g. "STOP"),
    /// or <c>null</c> when <see cref="IsOptOutKeyword"/> is <c>false</c>.
    /// </summary>
    public string? MatchedKeyword { get; init; }
}

/// <summary>
/// Detects TCPA opt-out keywords in an inbound SMS message body.
/// The seven keywords mandated by CTIA are: STOP, QUIT, END, REVOKE,
/// OPT-OUT, CANCEL, UNSUBSCRIBE.  Matching is case-insensitive and
/// word-boundary exact — substrings of longer words do not trigger opt-out.
/// </summary>
public interface IOptOutDetector
{
    /// <summary>
    /// Inspects <paramref name="messageBody"/> for the presence of any
    /// TCPA opt-out keyword on a word-boundary.
    /// </summary>
    /// <param name="messageBody">
    /// Raw inbound SMS text.  A <c>null</c> or empty value is treated as no
    /// match (returns <see cref="KeywordDetectionResult.IsOptOutKeyword"/>
    /// = <c>false</c>).
    /// </param>
    /// <returns>A <see cref="KeywordDetectionResult"/> describing whether
    /// a keyword was found and which keyword matched.</returns>
    KeywordDetectionResult Detect(string? messageBody);
}
