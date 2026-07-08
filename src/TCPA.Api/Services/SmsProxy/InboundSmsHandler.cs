using Microsoft.Extensions.Logging;
using TCPA.Api.Infrastructure;
using TCPA.Api.Infrastructure.Configuration;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Models;
using TCPA.Api.Services.OptOut;

namespace TCPA.Api.Services.SmsProxy;

/// <summary>
/// Processes inbound SMS webhook messages received from Cool Text (SPEC-002, SPEC-003).
///
/// Processing sequence:
/// 1. Resolve the originating SCG application from the Application Registry.
///    If no active registration is found, discard the message and emit a warning.
/// 2. Inspect the message body for TCPA opt-out keywords (<see cref="IOptOutDetector.Detect"/>).
/// 3a. Opt-out keyword detected:
///     - Write OPT_OUT status (<see cref="IOptOutStatusService.WriteOptOutAsync"/> — SPEC-004).
///     - If status write succeeds: dispatch confirmation SMS
///       (<see cref="IConfirmationDispatcher.DispatchAsync"/> — SPEC-005) and
///       write opt-out audit log entry (<see cref="IAuditLogService.WriteOptOutEventAsync"/> — SPEC-008).
///     - Forward original message to the originating application callback URL (SPEC-002).
/// 3b. No opt-out keyword:
///     - Forward message to the originating application callback URL only.
///
/// FAIL-CLOSED note: opt-out status write failure prevents the confirmation SMS from being sent
/// (SPEC-004 BR-017). The block on sending confirmation is enforced here, not by the dispatcher.
///
/// All cell phone numbers are logged as last 4 digits only (PII masking, BR-068).
/// </summary>
public sealed class InboundSmsHandler : IInboundSmsHandler
{
    private readonly IApplicationRegistryService _applicationRegistry;
    private readonly IOptOutDetector _optOutDetector;
    private readonly IOptOutStatusService _optOutStatusService;
    private readonly IConfirmationDispatcher _confirmationDispatcher;
    private readonly ICoolTextForwardingClient _coolTextForwardingClient;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<InboundSmsHandler> _logger;

    /// <summary>
    /// Initializes the inbound SMS handler with all required service dependencies.
    /// </summary>
    public InboundSmsHandler(
        IApplicationRegistryService applicationRegistry,
        IOptOutDetector optOutDetector,
        IOptOutStatusService optOutStatusService,
        IConfirmationDispatcher confirmationDispatcher,
        ICoolTextForwardingClient coolTextForwardingClient,
        IAuditLogService auditLogService,
        ILogger<InboundSmsHandler> logger)
    {
        _applicationRegistry = applicationRegistry ?? throw new ArgumentNullException(nameof(applicationRegistry));
        _optOutDetector = optOutDetector ?? throw new ArgumentNullException(nameof(optOutDetector));
        _optOutStatusService = optOutStatusService ?? throw new ArgumentNullException(nameof(optOutStatusService));
        _confirmationDispatcher = confirmationDispatcher ?? throw new ArgumentNullException(nameof(confirmationDispatcher));
        _coolTextForwardingClient = coolTextForwardingClient ?? throw new ArgumentNullException(nameof(coolTextForwardingClient));
        _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(InboundSmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var maskedNumber = MaskCellNumber(message.SenderCellNumber);
        var receiptTimestamp = DateTime.UtcNow;

        _logger.LogInformation(
            "Processing inbound SMS from ****{MaskedNumber}. Account: {AccountId}. MessageId: {MessageId}.",
            maskedNumber, message.CoolTextAccountId, message.CoolTextMessageId);

        // Step 1: Resolve the registered application for this Cool Text account.
        var application = await _applicationRegistry.GetByAccountNumberAsync(
            message.CoolTextAccountId, cancellationToken);

        if (application is null)
        {
            _logger.LogWarning(
                "Inbound SMS from ****{MaskedNumber} received for unregistered or inactive " +
                "Cool Text account {AccountId}. Message discarded.",
                maskedNumber, message.CoolTextAccountId);
            return;
        }

        // Step 2: Inspect for TCPA opt-out keywords (SPEC-003).
        var detection = _optOutDetector.Detect(message.MessageBody);

        if (detection.IsOptOutKeyword)
        {
            await HandleOptOutAsync(
                message, application, detection.MatchedKeyword!, maskedNumber, receiptTimestamp, cancellationToken);
        }
        else
        {
            await ForwardToApplicationAsync(message, application, maskedNumber, cancellationToken);
        }
    }

    /// <summary>
    /// Handles the opt-out processing pipeline for inbound messages containing a TCPA opt-out keyword.
    /// Sequence: write opt-out status → dispatch confirmation SMS → write audit entry → forward to application.
    /// Per BR-017, the confirmation SMS is sent ONLY if the status write succeeds.
    /// Per SPEC-002, the message is still forwarded to the application even when it contains an opt-out keyword.
    /// </summary>
    private async Task HandleOptOutAsync(
        InboundSmsMessage message,
        ApplicationRegistryEntry application,
        string matchedKeyword,
        string maskedNumber,
        DateTime receiptTimestamp,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Opt-out keyword '{Keyword}' detected in inbound SMS from ****{MaskedNumber}. " +
            "Account: {AccountId}. Writing OPT_OUT status.",
            matchedKeyword, maskedNumber, message.CoolTextAccountId);

        // Step 3a-i: Write the OPT_OUT status (SPEC-004). Must succeed before confirmation is sent (BR-017).
        WriteOptOutResult writeResult;
        try
        {
            writeResult = await _optOutStatusService.WriteOptOutAsync(
                message.SenderCellNumber,
                receiptTimestamp,
                application.ApplicationName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "CRITICAL: Failed to write OPT_OUT status for ****{MaskedNumber}. Account: {AccountId}. " +
                "Confirmation SMS will NOT be sent per BR-017. Audit alert required.",
                maskedNumber, message.CoolTextAccountId);

            // Do not send confirmation or audit entry for a successful opt-out — status was not written.
            // Still attempt to forward the original message to the application.
            await ForwardToApplicationAsync(message, application, maskedNumber, cancellationToken);
            return;
        }

        var systemResponse = writeResult.PreviousStatus == "OPT_OUT"
            ? "ALREADY_OPT_OUT_NO_ACTION"
            : "OPT_OUT_STATUS_WRITTEN";

        _logger.LogInformation(
            "OPT_OUT status write completed for ****{MaskedNumber}. PreviousStatus: {PreviousStatus}. " +
            "SystemResponse: {SystemResponse}.",
            maskedNumber, writeResult.PreviousStatus, systemResponse);

        // Step 3a-ii: Dispatch confirmation SMS (SPEC-005).
        // Per BR-015 and BR-023: only send confirmation if this is a NEW opt-out, not a duplicate.
        ConfirmationDispatchResult? confirmationResult = null;
        if (writeResult.PreviousStatus != "OPT_OUT")
        {
            confirmationResult = await DispatchConfirmationAsync(
                message, maskedNumber, receiptTimestamp, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Skipping confirmation SMS for ****{MaskedNumber}: number was already OPT_OUT (BR-023).",
                maskedNumber);
        }

        // Step 3a-iii: Write the opt-out audit log entry (SPEC-008).
        await WriteOptOutAuditEntryAsync(
            message, application, matchedKeyword, systemResponse, confirmationResult,
            receiptTimestamp, maskedNumber, cancellationToken);

        // Step 3a-iv: Forward original message to the application callback (SPEC-002).
        await ForwardToApplicationAsync(message, application, maskedNumber, cancellationToken);
    }

    /// <summary>
    /// Dispatches the opt-out confirmation SMS (SPEC-005).
    /// Per BR-025: a confirmation SMS delivery failure does NOT reverse the opt-out status.
    /// Logs the failure and returns the result regardless of outcome.
    /// </summary>
    private async Task<ConfirmationDispatchResult> DispatchConfirmationAsync(
        InboundSmsMessage message,
        string maskedNumber,
        DateTime receiptTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _confirmationDispatcher.DispatchAsync(
                message.SenderCellNumber,
                message.CoolTextAccountId,
                receiptTimestamp,
                cancellationToken);

            if (result.ConfirmationSent)
            {
                _logger.LogInformation(
                    "Opt-out confirmation SMS dispatched to ****{MaskedNumber}. " +
                    "SlaElapsedSeconds: {Elapsed}. MessageId: {MessageId}.",
                    maskedNumber, result.SlaElapsedSeconds, result.CoolTextMessageId);

                if (result.SlaElapsedSeconds > 60)
                {
                    _logger.LogWarning(
                        "OPT-OUT CONFIRMATION SLA BREACH: confirmation SMS for ****{MaskedNumber} " +
                        "dispatched {Elapsed}s after inbound receipt (SLA = 60s).",
                        maskedNumber, result.SlaElapsedSeconds);
                }
            }
            else
            {
                _logger.LogError(
                    "Opt-out confirmation SMS failed for ****{MaskedNumber}. " +
                    "OPT_OUT status is retained (BR-025).",
                    maskedNumber);
            }

            return result;
        }
        catch (Exception ex)
        {
            // BR-025: opt-out status is retained even if confirmation SMS fails.
            _logger.LogError(ex,
                "Exception dispatching opt-out confirmation SMS to ****{MaskedNumber}. " +
                "OPT_OUT status is retained (BR-025).",
                maskedNumber);

            return new ConfirmationDispatchResult
            {
                ConfirmationSent = false,
                SlaElapsedSeconds = (int)(DateTime.UtcNow - receiptTimestamp).TotalSeconds
            };
        }
    }

    /// <summary>
    /// Writes the opt-out event to the immutable audit log (SPEC-008).
    /// Per SPEC-008 BR-042: audit write failure is a critical error requiring an alert,
    /// but it does NOT roll back the opt-out status.
    /// </summary>
    private async Task WriteOptOutAuditEntryAsync(
        InboundSmsMessage message,
        ApplicationRegistryEntry application,
        string keyword,
        string systemResponse,
        ConfirmationDispatchResult? confirmationResult,
        DateTime receiptTimestamp,
        string maskedNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLogService.WriteOptOutEventAsync(
                cellPhoneNumber: message.SenderCellNumber,
                coolTextAccountId: message.CoolTextAccountId,
                applicationName: application.ApplicationName,
                keyword: keyword,
                messageBody: message.MessageBody,
                systemResponse: systemResponse,
                confirmationSent: confirmationResult?.ConfirmationSent ?? false,
                confirmationTimestamp: confirmationResult?.SendTimestamp,
                confirmationStatus: confirmationResult?.ConfirmationSent == true ? "SENT" : "FAILED",
                eventTimestamp: receiptTimestamp,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Opt-out audit log entry written for ****{MaskedNumber}. SystemResponse: {SystemResponse}.",
                maskedNumber, systemResponse);
        }
        catch (Exception ex)
        {
            // SPEC-008 BR-042: critical error — audit record is missing. Alert operations.
            _logger.LogCritical(ex,
                "CRITICAL: Opt-out audit log write FAILED for ****{MaskedNumber}. Account: {AccountId}. " +
                "Audit record is missing. Manual recovery required.",
                maskedNumber, message.CoolTextAccountId);
        }
    }

    /// <summary>
    /// Forwards the inbound SMS message to the registered application's callback URL
    /// using the Cool Text client (which implements retry with exponential backoff per SPEC-002).
    /// Logs a permanent delivery failure if all retries are exhausted; does not rethrow.
    /// </summary>
    private async Task ForwardToApplicationAsync(
        InboundSmsMessage message,
        ApplicationRegistryEntry application,
        string maskedNumber,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Forwarding inbound SMS from ****{MaskedNumber} to application {AppName} at {CallbackUrl}.",
            maskedNumber, application.ApplicationName, application.CallbackUrl);

        try
        {
            await _coolTextForwardingClient.ForwardToApplicationAsync(application.CallbackUrl, message);

            _logger.LogInformation(
                "Inbound SMS from ****{MaskedNumber} successfully forwarded to application {AppName}.",
                maskedNumber, application.ApplicationName);
        }
        catch (CoolTextForwardingException ex)
        {
            // All retries exhausted. Log permanent failure; no further action per SPEC-002.
            _logger.LogError(ex,
                "Permanent delivery failure: inbound SMS from ****{MaskedNumber} could not be forwarded " +
                "to application {AppName} at {CallbackUrl} after {AttemptCount} attempts.",
                maskedNumber, application.ApplicationName, ex.CallbackUrl, ex.AttemptCount);
        }
    }

    /// <summary>
    /// Returns the last 4 digits of a cell phone number for safe logging.
    /// Returns "****" if the number is null, empty, or shorter than 4 characters.
    /// </summary>
    private static string MaskCellNumber(string cellNumber)
    {
        if (string.IsNullOrEmpty(cellNumber) || cellNumber.Length < 4)
        {
            return "****";
        }
        return cellNumber[^4..];
    }
}
