using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Infrastructure.CoolText;

/// <summary>
/// Validates inbound Cool Text webhook requests using HMAC-SHA256 payload signing (ADR-007).
/// The Cool Text platform signs each webhook payload with a shared secret; this validator
/// recomputes the signature and rejects any request where the signatures do not match.
///
/// Fail-closed: any error during signature computation (missing secret, malformed header)
/// results in validation failure, never in a pass.
/// </summary>
public sealed class CoolTextWebhookValidator : ICoolTextWebhookValidator
{
    /// <summary>
    /// HTTP header name used by Cool Text to deliver the HMAC-SHA256 signature.
    /// Configurable via CoolText:WebhookSignatureHeader; defaults to the industry-standard value.
    /// Must be confirmed with the Cool Text vendor (STORY-003-SPIKE / ARCH-RISK-004).
    /// </summary>
    public const string DefaultSignatureHeader = "X-CoolText-Signature";

    private readonly byte[] _secretBytes;
    private readonly string _signatureHeader;
    private readonly ILogger<CoolTextWebhookValidator> _logger;

    /// <summary>
    /// Initializes the validator by loading the HMAC shared secret from configuration.
    /// Throws <see cref="InvalidOperationException"/> at startup if the secret is missing or empty,
    /// preventing the service from starting in a misconfigured state.
    /// </summary>
    /// <param name="configuration">Application configuration; must contain CoolText:WebhookSecret.</param>
    /// <param name="logger">Structured logger.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when CoolText:WebhookSecret is missing or empty.
    /// </exception>
    public CoolTextWebhookValidator(IConfiguration configuration, ILogger<CoolTextWebhookValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var secret = configuration["CoolText:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "CoolText:WebhookSecret is required but not configured. " +
                "Set this value in Azure Key Vault before starting the service.");
        }

        _secretBytes = Encoding.UTF8.GetBytes(secret);

        _signatureHeader = configuration["CoolText:WebhookSignatureHeader"]
                            ?? DefaultSignatureHeader;
    }

    /// <inheritdoc />
    public bool IsSignatureValid(string rawRequestBody, string? signatureHeaderValue)
    {
        if (signatureHeaderValue is null)
        {
            _logger.LogWarning(
                "Inbound webhook rejected: {Header} header is missing.",
                _signatureHeader);
            return false;
        }

        // Strip any prefix used by the vendor (e.g., "sha256=") before comparison.
        var receivedSignature = signatureHeaderValue.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeaderValue[7..]
            : signatureHeaderValue;

        string computedSignature;
        try
        {
            computedSignature = ComputeHmacSha256(rawRequestBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Inbound webhook rejected: HMAC computation failed. This may indicate a key loading problem.");
            return false;
        }

        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(receivedSignature));

        if (!valid)
        {
            _logger.LogWarning(
                "Inbound webhook rejected: HMAC-SHA256 signature mismatch. " +
                "Expected signature does not match {Header} header value.",
                _signatureHeader);
        }

        return valid;
    }

    /// <inheritdoc />
    public string SignatureHeaderName => _signatureHeader;

    /// <summary>
    /// Computes the HMAC-SHA256 signature of the given payload using the configured shared secret.
    /// Returns the signature as a lowercase hex string (no prefix).
    /// </summary>
    private string ComputeHmacSha256(string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(_secretBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

/// <summary>
/// Contract for validating Cool Text inbound webhook HMAC-SHA256 signatures.
/// </summary>
public interface ICoolTextWebhookValidator
{
    /// <summary>
    /// Validates that the provided signature header value matches the HMAC-SHA256
    /// of the raw request body computed with the configured shared secret.
    /// </summary>
    /// <param name="rawRequestBody">Raw UTF-8 body string exactly as received from Cool Text.</param>
    /// <param name="signatureHeaderValue">Value of the signature header; null if the header was absent.</param>
    /// <returns>
    /// <c>true</c> if the signature is valid; <c>false</c> if it is missing, malformed, or does not match.
    /// </returns>
    bool IsSignatureValid(string rawRequestBody, string? signatureHeaderValue);

    /// <summary>The HTTP header name the validator reads the signature from.</summary>
    string SignatureHeaderName { get; }
}
