using Microsoft.Extensions.Logging;
using TCPA.Api.Infrastructure.Configuration;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Models;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.OptOut;

namespace TCPA.Api.Services.SmsProxy;

/// <summary>
/// TCPA compliance gate for outbound SMS messages (SPEC-001, SPEC-006).
///
/// Processing sequence:
/// 1. Resolve the Application Registry entry for the provided Cool Text account ID.
///    If the account is unregistered or inactive, return <see cref="OutboundGateDecision.UnregisteredAccount"/>
///    without enforcement and without logging a compliance event (BR-004, SPEC-014).
/// 2. Query the opt-out status database for the destination cell number via
///    <see cref="IOptOutStatusService.IsOptedOutAsync"/>.
///    FAIL-CLOSED (NFS-005): any exception during the status check throws
///    <see cref="OutboundGateUnavailableException"/>. The controller must return 503.
///    No message is ever forwarded without a confirmed status read.
/// 3. If OPT_OUT: suppress the message, write the blocked-outbound audit log entry (SPEC-009),
///    and return <see cref="OutboundGateDecision.Suppressed"/>.
/// 4. If OPT_IN (or no record — defaults to OPT_IN per BR-001): forward the message to
///    Cool Text via <see cref="ICoolTextClient.SendSmsAsync"/> and return
///    <see cref="OutboundGateDecision.Forwarded"/> with the Cool Text message ID.
///
/// All cell phone numbers are logged as last 4 digits only (PII masking, BR-068).
/// </summary>
public sealed class OutboundSmsGate : IOutboundSmsGate
{
    private readonly IApplicationRegistryService _applicationRegistry;
    private readonly IOptOutStatusService _optOutStatusService;
    private readonly ICoolTextClient _coolTextClient;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<OutboundSmsGate> _logger;

    /// <summary>
    /// Initializes the outbound SMS compliance gate with required service dependencies.
    /// </summary>
    public OutboundSmsGate(
        IApplicationRegistryService applicationRegistry,
        IOptOutStatusService optOutStatusService,
        ICoolTextClient coolTextClient,
        IAuditLogService auditLogService,
        ILogger<OutboundSmsGate> logger)
    {
        _applicationRegistry = applicationRegistry ?? throw new ArgumentNullException(nameof(applicationRegistry));
        _optOutStatusService = optOutStatusService ?? throw new ArgumentNullException(nameof(optOutStatusService));
        _coolTextClient = coolTextClient ?? throw new ArgumentNullException(nameof(coolTextClient));
        _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="OutboundGateUnavailableException">
    /// Thrown if the opt-out status database is unavailable (fail-closed, NFS-005).
    /// The controller must return 503 Service Unavailable.
    /// </exception>
    /// <exception cref="CoolTextApiException">
    /// Thrown if Cool Text is unreachable after opt-in was confirmed.
    /// The controller must return 502 Bad Gateway.
    /// </exception>
    public async Task<OutboundGateResult> ProcessAsync(
        OutboundSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var maskedNumber = MaskCellNumber(request.DestinationCellNumber);
        var gateTimestamp = DateTime.UtcNow;

        _logger.LogInformation(
            "Outbound SMS compliance gate: evaluating request for ****{MaskedNumber}. Account: {AccountId}.",
            maskedNumber, request.CoolTextAccountId);

        // Step 1: Resolve the application registration.
        var application = await _applicationRegistry.GetByAccountNumberAsync(
            request.CoolTextAccountId, cancellationToken);

        if (application is null)
        {
            _logger.LogInformation(
                "Outbound SMS for account {AccountId} is unregistered or inactive. " +
                "Passing through without TCPA enforcement (BR-004, SPEC-014).",
                request.CoolTextAccountId);
            return OutboundGateResult.UnregisteredAccount();
        }

        // Step 2: Check opt-out status. FAIL-CLOSED: any exception blocks the message and returns 503.
        bool isOptedOut;
        try
        {
            isOptedOut = await _optOutStatusService.IsOptedOutAsync(
                request.DestinationCellNumber, cancellationToken);

            _logger.LogInformation(
                "Opt-out status for ****{MaskedNumber}: {Status}. Account: {AccountId}.",
                maskedNumber, isOptedOut ? "OPT_OUT" : "OPT_IN", request.CoolTextAccountId);
        }
        catch (Exception ex)
        {
            // FAIL-CLOSED: cannot confirm opt-out status — block the message (NFS-005).
            _logger.LogCritical(ex,
                "FAIL-CLOSED: Opt-out status check failed for ****{MaskedNumber}. Account: {AccountId}. " +
                "Message blocked per NFS-005. Returning 503.",
                maskedNumber, request.CoolTextAccountId);

            throw new OutboundGateUnavailableException(
                $"TCPA opt-out status unavailable for destination ****{maskedNumber}. Message not forwarded.",
                ex);
        }

        // Step 3: OPT_OUT — suppress the message and write the blocked-outbound audit log entry.
        if (isOptedOut)
        {
            _logger.LogInformation(
                "Outbound SMS to ****{MaskedNumber} SUPPRESSED: OPT_OUT status confirmed. Account: {AccountId}.",
                maskedNumber, request.CoolTextAccountId);

            await WriteBlockedOutboundAuditEntryAsync(
                request, application, maskedNumber, gateTimestamp, cancellationToken);

            return OutboundGateResult.Suppressed();
        }

        // Step 4: OPT_IN (or no record — defaults to OPT_IN per BR-001). Forward to Cool Text.
        SendSmsResult sendResult;
        try
        {
            sendResult = await _coolTextClient.SendSmsAsync(
                request.CoolTextAccountId,
                request.DestinationCellNumber,
                request.MessageBody,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Cool Text is unreachable after opt-in was confirmed.
            // This is a 502 Bad Gateway condition (not fail-closed 503) — rethrow for the controller.
            _logger.LogError(ex,
                "Cool Text API error when forwarding SMS to ****{MaskedNumber}. Account: {AccountId}.",
                maskedNumber, request.CoolTextAccountId);
            throw;
        }

        _logger.LogInformation(
            "Outbound SMS to ****{MaskedNumber} FORWARDED to Cool Text. Account: {AccountId}. " +
            "MessageId: {MessageId}.",
            maskedNumber, request.CoolTextAccountId, sendResult.MessageId);

        return OutboundGateResult.Forwarded(sendResult.MessageId);
    }

    /// <summary>
    /// Writes a blocked-outbound audit log entry (SPEC-009).
    /// Per SPEC-009 BR-048: the message is suppressed regardless of whether the audit write succeeds.
    /// A write failure is logged as a Critical error requiring manual alert and investigation.
    /// </summary>
    private async Task WriteBlockedOutboundAuditEntryAsync(
        OutboundSmsRequest request,
        ApplicationRegistryEntry application,
        string maskedNumber,
        DateTime gateTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLogService.WriteBlockedOutboundEventAsync(
                cellPhoneNumber: request.DestinationCellNumber,
                coolTextAccountId: request.CoolTextAccountId,
                applicationName: application.ApplicationName,
                messageBody: request.MessageBody,
                eventTimestamp: gateTimestamp,
                cancellationToken: cancellationToken);
        }
        catch (Exception auditEx)
        {
            // SPEC-009 BR-048: suppression is enforced regardless. Block is NOT reversed.
            _logger.LogCritical(auditEx,
                "CRITICAL: Failed to write blocked-outbound audit log entry for ****{MaskedNumber}. " +
                "Account: {AccountId}. Application: {AppName}. " +
                "Message is suppressed but audit record is missing.",
                maskedNumber, request.CoolTextAccountId, application.ApplicationName);
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
