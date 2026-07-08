// src/TCPA.Api/Controllers/AdminController.cs
// TCPA Compliance Engine — Admin API Controller (Re-Opt-In endpoints)
// Source: TASK-026, TASK-029 | SPEC-007, SPEC-010 | STORY-009, STORY-010
// Business Rules: BR-031 through BR-038

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TCPA.Api.Services.ReOptIn;

namespace TCPA.Api.Controllers
{
    // ---------------------------------------------------------------------------
    // Request / response DTOs
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Request body for <c>POST /api/v1/admin/reopt-in</c>.
    /// </summary>
    public sealed class ReOptInRequest
    {
        /// <summary>E.164 cell phone number of the customer to re-opt-in.</summary>
        [Required(ErrorMessage = "cellPhoneNumber is required.")]
        [RegularExpression(@"^\+[1-9]\d{1,14}$",
            ErrorMessage = "cellPhoneNumber must be in E.164 format (e.g. +12025551234).")]
        [JsonPropertyName("cellPhoneNumber")]
        public string CellPhoneNumber { get; init; } = string.Empty;

        /// <summary>
        /// Mandatory free-text reason for the re-opt-in action; minimum 20 characters.
        /// </summary>
        [Required(ErrorMessage = "reason is required.")]
        [MinLength(20, ErrorMessage = "reason must be at least 20 characters.")]
        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;

        /// <summary>Optional Help Desk ticket reference.</summary>
        [JsonPropertyName("ticketReference")]
        public string? TicketReference { get; init; }
    }

    /// <summary>
    /// Response body for a successful <c>POST /api/v1/admin/reopt-in</c>.
    /// </summary>
    public sealed class ReOptInResponse
    {
        /// <summary><c>true</c> when the status was set to OPT-IN.</summary>
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        /// <summary>Status before this call: "OPT_IN" or "OPT_OUT".</summary>
        [JsonPropertyName("previousStatus")]
        public string PreviousStatus { get; init; } = string.Empty;

        /// <summary>Status after this call: always "OPT_IN".</summary>
        [JsonPropertyName("newStatus")]
        public string NewStatus { get; init; } = string.Empty;

        /// <summary>ISO 8601 UTC timestamp of the update.</summary>
        [JsonPropertyName("updatedTimestamp")]
        public DateTime UpdatedTimestamp { get; init; }

        /// <summary>Audit record ID for the re-opt-in event.</summary>
        [JsonPropertyName("recordId")]
        public Guid? RecordId { get; init; }

        /// <summary>Informational message (e.g. no-op note for idempotent call).</summary>
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Response body for <c>GET /api/v1/admin/status/{cellPhoneNumber}</c>.
    /// </summary>
    public sealed class OptOutStatusResponse
    {
        /// <summary>Masked cell number — last four digits only (BR-037).</summary>
        [JsonPropertyName("maskedCellNumber")]
        public string MaskedCellNumber { get; init; } = string.Empty;

        /// <summary>Current status: "OPT_IN" or "OPT_OUT".</summary>
        [JsonPropertyName("optOutStatus")]
        public string OptOutStatus { get; init; } = string.Empty;

        /// <summary>Timestamp of the most recent opt-out, or <c>null</c>.</summary>
        [JsonPropertyName("lastOptOutTimestamp")]
        public DateTime? LastOptOutTimestamp { get; init; }

        /// <summary>Timestamp of the most recent re-opt-in, or <c>null</c>.</summary>
        [JsonPropertyName("lastOptInTimestamp")]
        public DateTime? LastOptInTimestamp { get; init; }
    }

    // ---------------------------------------------------------------------------
    // Controller
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Admin API controller for the TCPA Re-Opt-In workflow.
    /// All endpoints require an authenticated Bearer token with the
    /// <c>tcpa.helpdesk</c> or <c>tcpa.compliance_officer</c> role claim
    /// (BR-031, BR-032).
    /// </summary>
    [ApiController]
    [Route("admin/v1/opt-out")]
    [Authorize(Roles = "tcpa.helpdesk,tcpa.compliance_officer")]
    public sealed class AdminController : ControllerBase
    {
        private readonly IReOptInService _reOptInService;
        private readonly ILogger<AdminController> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="AdminController"/>.
        /// </summary>
        /// <param name="reOptInService">Re-opt-in business logic service.</param>
        /// <param name="logger">Structured logger.</param>
        public AdminController(IReOptInService reOptInService, ILogger<AdminController> logger)
        {
            _reOptInService = reOptInService ?? throw new ArgumentNullException(nameof(reOptInService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // -------------------------------------------------------------------
        // PUT /admin/v1/opt-out/re-opt-in  (architecture.md ADR contract)
        // -------------------------------------------------------------------

        /// <summary>
        /// Manually re-opts-in a cell phone number.
        /// Only authorized Help Desk agents and Compliance Officers may call
        /// this endpoint.  The agent user ID is extracted from the JWT token,
        /// not from the request body (BR-038 / TASK-029).
        /// </summary>
        /// <remarks>
        /// <list type="table">
        ///   <item><term>200</term><description>Success (including idempotent already-OPT-IN case).</description></item>
        ///   <item><term>400</term><description>Validation failure (missing/invalid field).</description></item>
        ///   <item><term>401</term><description>Missing or invalid Bearer token.</description></item>
        ///   <item><term>403</term><description>Valid token without the required role.</description></item>
        ///   <item><term>409</term><description>No prior opt-out record exists for this number.</description></item>
        ///   <item><term>503</term><description>Database unavailable.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="request">Re-opt-in payload.</param>
        /// <param name="cancellationToken">Propagates cancellation.</param>
        /// <returns>A <see cref="ReOptInResponse"/> or a problem detail on error.</returns>
        [HttpPut("re-opt-in")]
        [ProducesResponseType(typeof(ReOptInResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> ReOptIn(
            [FromBody] ReOptInRequest request,
            CancellationToken cancellationToken)
        {
            // Agent user ID comes from the validated JWT token — never from the request
            // body, to prevent spoofing (TASK-029 spec note).
            string agentUserId = User.Identity?.Name
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("oid")?.Value
                ?? "unknown";

            string maskedNumber = MaskPhoneNumber(request.CellPhoneNumber);

            // Log every call as a security event regardless of outcome (BR-032 / TASK-029).
            _logger.LogInformation(
                "SECURITY_EVENT: Admin re-opt-in called for number ****{Masked} by agent {AgentId}.",
                maskedNumber, agentUserId);

            ReOptInResult result;

            try
            {
                result = await _reOptInService.ReOptInAsync(
                    cellPhoneNumber: request.CellPhoneNumber,
                    requestedBy: agentUserId,
                    reason: request.Reason,
                    ticketReference: request.TicketReference,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    "Re-opt-in validation failure for number ****{Masked} by agent {AgentId}: {Message}",
                    maskedNumber, agentUserId, ex.Message);

                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Re-opt-in service error for number ****{Masked} by agent {AgentId}.",
                    maskedNumber, agentUserId);

                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = "Service Unavailable",
                    Detail = "The TCPA compliance service is temporarily unavailable. Please try again shortly.",
                    Status = StatusCodes.Status503ServiceUnavailable,
                });
            }

            // No prior opt-out record — 409 Conflict (BR-038).
            if (result.PreviousStatus == ReOptInService.NoRecordStatus)
            {
                _logger.LogWarning(
                    "SECURITY_EVENT: Re-opt-in rejected (409) for number ****{Masked} — no prior opt-out record. Agent: {AgentId}.",
                    maskedNumber, agentUserId);

                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = result.Message,
                    Status = StatusCodes.Status409Conflict,
                });
            }

            _logger.LogInformation(
                "SECURITY_EVENT: Re-opt-in {Outcome} for number ****{Masked} by agent {AgentId}. " +
                "Previous: {Previous}, New: {New}.",
                result.Success ? "SUCCEEDED" : "FAILED",
                maskedNumber, agentUserId, result.PreviousStatus, result.NewStatus);

            return Ok(new ReOptInResponse
            {
                Success = result.Success,
                PreviousStatus = result.PreviousStatus,
                NewStatus = result.NewStatus,
                UpdatedTimestamp = result.UpdatedTimestamp,
                RecordId = result.RecordId,
                Message = result.Message,
            });
        }

        // -------------------------------------------------------------------
        // GET /api/v1/admin/status/{cellPhoneNumber}
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the current opt-out status for a cell phone number.
        /// The response returns only the last four digits of the cell number
        /// to minimise PII exposure in logs (BR-037).
        /// </summary>
        /// <remarks>
        /// <list type="table">
        ///   <item><term>200</term><description>Record found; status returned with masked number.</description></item>
        ///   <item><term>400</term><description>Invalid E.164 cell phone number format.</description></item>
        ///   <item><term>401</term><description>Missing or invalid Bearer token.</description></item>
        ///   <item><term>403</term><description>Valid token without the required role.</description></item>
        ///   <item><term>404</term><description>No record exists for this cell number.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="cellPhoneNumber">
        /// E.164 cell phone number (URL-encoded, path parameter).
        /// </param>
        /// <param name="cancellationToken">Propagates cancellation.</param>
        /// <returns>An <see cref="OptOutStatusResponse"/> or a problem detail on error.</returns>
        [HttpGet("status/{cellPhoneNumber}")]
        [ProducesResponseType(typeof(OptOutStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus(
            [FromRoute] string cellPhoneNumber,
            CancellationToken cancellationToken)
        {
            // Validate E.164 format at the controller boundary.
            if (!IsValidE164(cellPhoneNumber))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "cellPhoneNumber must be in E.164 format (e.g. +12025551234).",
                    Status = StatusCodes.Status400BadRequest,
                });
            }

            string maskedNumber = MaskPhoneNumber(cellPhoneNumber);

            _logger.LogInformation(
                "Admin status lookup for number ****{Masked} by agent {AgentId}.",
                maskedNumber, User.Identity?.Name ?? "unknown");

            OptOutStatusResult? statusResult = await _reOptInService
                .GetStatusAsync(cellPhoneNumber, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult is null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Not Found",
                    Detail = "No opt-out record exists for this cell number.",
                    Status = StatusCodes.Status404NotFound,
                });
            }

            return Ok(new OptOutStatusResponse
            {
                MaskedCellNumber = statusResult.MaskedCellNumber,
                OptOutStatus = statusResult.OptOutStatus,
                LastOptOutTimestamp = statusResult.LastOptOutTimestamp,
                LastOptInTimestamp = statusResult.LastOptInTimestamp,
            });
        }

        // -------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> when <paramref name="value"/> matches the E.164
        /// phone number pattern.
        /// </summary>
        private static bool IsValidE164(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(
                value, @"^\+[1-9]\d{1,14}$");
        }

        /// <summary>
        /// Returns the last four digits of a phone number prefixed with asterisks
        /// (BR-068 / NFS-007c).
        /// </summary>
        private static string MaskPhoneNumber(string phoneNumber)
        {
            return phoneNumber?.Length >= 4
                ? "****" + phoneNumber[^4..]
                : "****";
        }
    }
}
