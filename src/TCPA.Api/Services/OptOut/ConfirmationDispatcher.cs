// src/TCPA.Api/Services/OptOut/ConfirmationDispatcher.cs
// TCPA Compliance Engine — Opt-Out Confirmation SMS Dispatcher Implementation
// Source: TASK-021 | SPEC-005 | STORY-006
// Business Rules: BR-021 through BR-026

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TCPA.Api.Infrastructure.CoolText;

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Sends the Legal/Compliance-approved opt-out confirmation SMS to the
/// opted-out customer within the 60-second TCPA SLA window (NFS-001).
/// </summary>
/// <remarks>
/// <para>
/// The confirmation text is loaded from Azure Key Vault / Azure App
/// Configuration under the key <c>TCPA:OptOutConfirmationSmsText</c>.
/// It is never hardcoded in the application binary (SPEC-A-011).
/// </para>
/// <para>
/// On Cool Text delivery failure, one retry is performed after a 2-second
/// delay.  If both attempts fail, the failure is logged as a permanent error
/// but the opt-out status is NOT reversed (BR-025).
/// </para>
/// </remarks>
public sealed class ConfirmationDispatcher : IConfirmationDispatcher
{
    /// <summary>
    /// Configuration key for the Legal/Compliance-approved confirmation SMS text.
    /// </summary>
    private const string ConfirmationTextConfigKey = "TCPA:OptOutConfirmationSmsText";

    /// <summary>
    /// TCPA SLA ceiling in seconds (NFS-001).
    /// </summary>
    private const int SlaCeilingSeconds = 60;

    /// <summary>
    /// Delay before a single retry on Cool Text failure (SPEC-005 spec note).
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly ICoolTextClient _coolTextClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfirmationDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ConfirmationDispatcher"/>.
    /// </summary>
    /// <param name="coolTextClient">Abstraction over the Cool Text / Twilio API.</param>
    /// <param name="configuration">Application configuration (provides the confirmation SMS text).</param>
    /// <param name="logger">Structured logger.</param>
    public ConfirmationDispatcher(
        ICoolTextClient coolTextClient,
        IConfiguration configuration,
        ILogger<ConfirmationDispatcher> logger)
    {
        _coolTextClient = coolTextClient ?? throw new ArgumentNullException(nameof(coolTextClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ConfirmationDispatchResult> DispatchAsync(
        string cellPhoneNumber,
        string coolTextAccountId,
        DateTime inboundReceiptTimestamp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cellPhoneNumber))
            throw new ArgumentException("Cell phone number must not be null or whitespace.", nameof(cellPhoneNumber));
        if (string.IsNullOrWhiteSpace(coolTextAccountId))
            throw new ArgumentException("Cool Text account ID must not be null or whitespace.", nameof(coolTextAccountId));

        string maskedNumber = MaskPhoneNumber(cellPhoneNumber);

        string? confirmationText = _configuration[ConfirmationTextConfigKey];
        if (string.IsNullOrWhiteSpace(confirmationText))
        {
            _logger.LogCritical(
                "Configuration key '{ConfigKey}' is missing or empty. " +
                "Opt-out confirmation SMS cannot be sent for number ****{Masked}. " +
                "Legal/Compliance must supply the approved message text.",
                ConfirmationTextConfigKey, maskedNumber);

            return new ConfirmationDispatchResult
            {
                ConfirmationSent = false,
                CoolTextMessageId = null,
                SendTimestamp = null,
                SlaElapsedSeconds = ComputeElapsedSeconds(inboundReceiptTimestamp),
            };
        }

        int elapsedAtDispatch = ComputeElapsedSeconds(inboundReceiptTimestamp);
        if (elapsedAtDispatch > SlaCeilingSeconds)
        {
            _logger.LogError(
                "SLA BREACH: Opt-out confirmation for number ****{Masked} is being dispatched " +
                "{ElapsedSeconds}s after inbound receipt — exceeds 60-second TCPA SLA (NFS-001). " +
                "Account: {AccountId}.",
                maskedNumber, elapsedAtDispatch, coolTextAccountId);
        }

        // Attempt dispatch; one retry on failure (SPEC-005 edge case handling).
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    _logger.LogWarning(
                        "Retrying opt-out confirmation SMS for number ****{Masked} " +
                        "(attempt {Attempt}/2) after 2-second delay.",
                        maskedNumber, attempt);

                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }

                // ICoolTextClient.SendSmsAsync returns the platform message ID string.
                string messageId = await _coolTextClient.SendSmsAsync(
                    coolTextAccountId,
                    cellPhoneNumber,
                    confirmationText).ConfigureAwait(false);

                DateTime sendTimestamp = DateTime.UtcNow;
                int finalElapsed = ComputeElapsedSeconds(inboundReceiptTimestamp);

                _logger.LogInformation(
                    "Opt-out confirmation SMS sent for number ****{Masked} " +
                    "(CoolText message {MessageId}), SLA elapsed {ElapsedSeconds}s.",
                    maskedNumber, messageId, finalElapsed);

                return new ConfirmationDispatchResult
                {
                    ConfirmationSent = true,
                    CoolTextMessageId = messageId,
                    SendTimestamp = sendTimestamp,
                    SlaElapsedSeconds = finalElapsed,
                };
            }
            catch (OperationCanceledException)
            {
                // Propagate cancellation — do not swallow.
                throw;
            }
            catch (Exception ex) when (attempt == 1)
            {
                _logger.LogWarning(
                    ex,
                    "Opt-out confirmation SMS attempt 1 failed for number ****{Masked} " +
                    "via account {AccountId}. Will retry once.",
                    maskedNumber, coolTextAccountId);
            }
            catch (Exception ex)
            {
                // Both attempts failed — log permanent failure.
                // The opt-out status is already written; we do NOT reverse it (BR-025).
                _logger.LogError(
                    ex,
                    "PERMANENT FAILURE: Opt-out confirmation SMS could not be delivered " +
                    "for number ****{Masked} via account {AccountId} after 2 attempts. " +
                    "Opt-out status remains OPT-OUT.",
                    maskedNumber, coolTextAccountId);

                return new ConfirmationDispatchResult
                {
                    ConfirmationSent = false,
                    CoolTextMessageId = null,
                    SendTimestamp = null,
                    SlaElapsedSeconds = ComputeElapsedSeconds(inboundReceiptTimestamp),
                };
            }
        }

        // Unreachable — all paths above return or throw within the loop.
        // Included as a defensive measure to satisfy the compiler.
        return new ConfirmationDispatchResult
        {
            ConfirmationSent = false,
            CoolTextMessageId = null,
            SendTimestamp = null,
            SlaElapsedSeconds = ComputeElapsedSeconds(inboundReceiptTimestamp),
        };
    }

    /// <summary>
    /// Computes the whole seconds elapsed since <paramref name="receiptTimestamp"/>.
    /// </summary>
    private static int ComputeElapsedSeconds(DateTime receiptTimestamp)
    {
        return (int)(DateTime.UtcNow - receiptTimestamp).TotalSeconds;
    }

    /// <summary>
    /// Returns the last four digits of a phone number, prefixed with asterisks
    /// (BR-068 / NFS-007c).
    /// </summary>
    private static string MaskPhoneNumber(string phoneNumber)
    {
        return phoneNumber.Length >= 4
            ? "****" + phoneNumber[^4..]
            : "****";
    }
}
