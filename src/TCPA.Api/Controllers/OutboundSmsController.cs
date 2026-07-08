using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TCPA.Api.Infrastructure.Auth;
using TCPA.Api.Models;
using TCPA.Api.Services.SmsProxy;

namespace TCPA.Api.Controllers;

/// <summary>
/// Receives outbound SMS requests from upstream SCG applications and enforces the TCPA
/// compliance gate before forwarding to Cool Text/Twilio (SPEC-001, SPEC-006).
///
/// Authentication: API Key via X-API-Key header (ADR-006). The API key middleware
/// validates the key and issues 401 before this controller action is invoked.
///
/// Fail-closed behavior (NFS-005): if the TCPA opt-out database is unavailable,
/// this endpoint returns 503 Service Unavailable. No message is forwarded without
/// a confirmed opt-out status read.
/// </summary>
[ApiController]
[Route("api/v1/sms")]
[ApiKeyAuthFilter]
public sealed class OutboundSmsController : ControllerBase
{
    private readonly IOutboundSmsGate _outboundSmsGate;
    private readonly ILogger<OutboundSmsController> _logger;

    /// <summary>
    /// Initializes the outbound SMS controller with the compliance gate dependency.
    /// </summary>
    public OutboundSmsController(
        IOutboundSmsGate outboundSmsGate,
        ILogger<OutboundSmsController> logger)
    {
        _outboundSmsGate = outboundSmsGate ?? throw new ArgumentNullException(nameof(outboundSmsGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Receives an outbound SMS request from an upstream SCG application, checks the
    /// destination cell number against the TCPA opt-out database, and either forwards
    /// the message to Cool Text or suppresses it.
    ///
    /// Authentication: X-API-Key header (validated by middleware before this action executes).
    ///
    /// Response status codes:
    /// <list type="bullet">
    ///   <item>200 — gate decision made (status field: FORWARDED, SUPPRESSED, or UNREGISTERED_ACCOUNT).</item>
    ///   <item>400 — missing required field or invalid E.164 destination number.</item>
    ///   <item>401 — missing or invalid X-API-Key header (returned by middleware).</item>
    ///   <item>502 — Cool Text/Twilio unreachable (only after opt-in check passed).</item>
    ///   <item>503 — TCPA database unavailable; fail-closed; message not forwarded.</item>
    /// </list>
    /// </summary>
    /// <param name="request">Outbound SMS request from the upstream SCG application.</param>
    /// <returns>
    /// <see cref="SmsResponse"/> with the compliance gate decision, or an error response.
    /// </returns>
    [HttpPost("outbound")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SmsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SmsErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(SmsErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(SmsErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendOutbound([FromBody] OutboundSmsRequest request)
    {
        if (!ModelState.IsValid)
        {
            // Collect field names for the structured 400 error per SPEC-001 error contract.
            var invalidFields = ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .Select(kv => kv.Key)
                .ToList();

            _logger.LogWarning(
                "Outbound SMS request rejected: validation failure on fields [{Fields}]. " +
                "CorrelationId: {CorrelationId}.",
                string.Join(", ", invalidFields),
                HttpContext.TraceIdentifier);

            return BadRequest(new SmsErrorResponse
            {
                Error = "VALIDATION_ERROR",
                Fields = invalidFields
            });
        }

        var maskedNumber = MaskCellNumber(request.DestinationCellNumber);

        _logger.LogInformation(
            "Outbound SMS request received. Destination: ****{MaskedNumber}. Account: {AccountId}. " +
            "CorrelationId: {CorrelationId}.",
            maskedNumber, request.CoolTextAccountId, HttpContext.TraceIdentifier);

        OutboundGateResult gateResult;
        try
        {
            gateResult = await _outboundSmsGate.ProcessAsync(request, HttpContext.RequestAborted);
        }
        catch (OutboundGateUnavailableException ex)
        {
            // FAIL-CLOSED (NFS-005): opt-out status check failed — block and return 503.
            _logger.LogCritical(ex,
                "Outbound SMS BLOCKED (fail-closed): TCPA database unavailable for ****{MaskedNumber}. " +
                "Account: {AccountId}. CorrelationId: {CorrelationId}.",
                maskedNumber, request.CoolTextAccountId, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, SmsErrorResponse.ServiceUnavailable());
        }
        catch (Exception ex)
        {
            // All other exceptions from the gate (including Cool Text API errors) map to 502.
            _logger.LogError(ex,
                "Outbound SMS failed: unexpected error for ****{MaskedNumber}. " +
                "Account: {AccountId}. CorrelationId: {CorrelationId}.",
                maskedNumber, request.CoolTextAccountId, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status502BadGateway, SmsErrorResponse.BadGateway());
        }

        var response = gateResult.Decision switch
        {
            OutboundGateDecision.Forwarded => SmsResponse.Forwarded(gateResult.MessageId!),
            OutboundGateDecision.Suppressed => SmsResponse.Suppressed(),
            OutboundGateDecision.UnregisteredAccount => SmsResponse.UnregisteredAccount(),
            _ => throw new InvalidOperationException(
                $"Unexpected OutboundGateDecision value: {gateResult.Decision}")
        };

        _logger.LogInformation(
            "Outbound SMS compliance decision: {Status}. Destination: ****{MaskedNumber}. " +
            "Account: {AccountId}. CorrelationId: {CorrelationId}.",
            response.Status, maskedNumber, request.CoolTextAccountId, HttpContext.TraceIdentifier);

        return Ok(response);
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
