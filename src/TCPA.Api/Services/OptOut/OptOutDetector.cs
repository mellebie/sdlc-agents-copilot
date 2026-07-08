// src/TCPA.Api/Services/OptOut/OptOutDetector.cs
// TCPA Compliance Engine — Opt-Out Keyword Detection Implementation
// Source: TASK-015 | SPEC-003 | STORY-004
// Business Rules: BR-010 through BR-015

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Pure, stateless implementation of <see cref="IOptOutDetector"/>.
/// Detects the seven CTIA-mandated opt-out keywords using pre-compiled,
/// case-insensitive, word-boundary regular expressions.
/// </summary>
/// <remarks>
/// <para>
/// The seven keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL,
/// UNSUBSCRIBE.  "OPT-OUT" is matched as a single hyphenated token;
/// "OPT" alone does not trigger opt-out (BR-013).
/// </para>
/// <para>
/// This class has no external dependencies and is safe to register as a
/// singleton in the DI container.
/// </para>
/// </remarks>
public sealed class OptOutDetector : IOptOutDetector
{
    private readonly ILogger<OptOutDetector> _logger;

    /// <summary>
    /// Pre-compiled keyword patterns ordered so that the more specific
    /// "OPT-OUT" is evaluated before any simple "OPT" substring concern.
    /// Each tuple contains (normalizedKeyword, compiledRegex).
    /// </summary>
    private static readonly (string Keyword, Regex Pattern)[] KeywordPatterns =
    [
        ("STOP",        new Regex(@"\bSTOP\b",        RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("QUIT",        new Regex(@"\bQUIT\b",        RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("END",         new Regex(@"\bEND\b",         RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("REVOKE",      new Regex(@"\bREVOKE\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("OPT-OUT",     new Regex(@"\bOPT-OUT\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("CANCEL",      new Regex(@"\bCANCEL\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("UNSUBSCRIBE", new Regex(@"\bUNSUBSCRIBE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    ];

    /// <summary>
    /// Initializes a new instance of <see cref="OptOutDetector"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public OptOutDetector(ILogger<OptOutDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public KeywordDetectionResult Detect(string? messageBody)
    {
        if (string.IsNullOrEmpty(messageBody))
        {
            _logger.LogWarning(
                "OptOutDetector received a null or empty message body; treating as no opt-out keyword.");
            return new KeywordDetectionResult { IsOptOutKeyword = false, MatchedKeyword = null };
        }

        foreach (var (keyword, pattern) in KeywordPatterns)
        {
            if (pattern.IsMatch(messageBody))
            {
                _logger.LogInformation(
                    "Opt-out keyword {Keyword} detected in inbound SMS (word-boundary match).",
                    keyword);
                return new KeywordDetectionResult { IsOptOutKeyword = true, MatchedKeyword = keyword };
            }
        }

        return new KeywordDetectionResult { IsOptOutKeyword = false, MatchedKeyword = null };
    }
}
