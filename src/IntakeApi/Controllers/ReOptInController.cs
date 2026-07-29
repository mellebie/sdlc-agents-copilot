using ConsentService.Models;
using ConsentService.Services;
using IntakeApi.Contracts;
using IntakeApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace IntakeApi.Controllers;

[Route("api/v1/consent/reoptin")]
[Produces("application/json")]
public sealed class ReOptInController : ControllerBase
{
    private readonly IReOptInService _reOptInService;
    private readonly ICorrelationIdGenerator _correlationIdGenerator;

    public ReOptInController(IReOptInService reOptInService, ICorrelationIdGenerator correlationIdGenerator)
    {
        _reOptInService = reOptInService;
        _correlationIdGenerator = correlationIdGenerator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReOptInResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ReOptInResponse>> Post([FromBody] ReOptInRequest? request, CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdGenerator.Generate();
        if (!IsValid(request))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "INVALID_REOPTIN_REQUEST",
                Message = "Re-opt-in request payload is invalid.",
                CorrelationId = correlationId
            });
        }

        var proof = Request.Headers["X-ReOptIn-Proof"].FirstOrDefault();
        var nonce = Request.Headers["X-Request-Nonce"].FirstOrDefault();

        var result = await _reOptInService.ProcessAsync(new ReOptInTransitionRequest(
            request!.ReOptInRequestId!,
            request.CustomerPhoneNumber!,
            request.InitiationChannel!.Value,
            request.InitiatedAtUtc!.Value,
            proof,
            nonce), cancellationToken);

        if (!result.Success)
        {
            if (result.Code == "INVALID_REOPTIN_CHANNEL")
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "INVALID_REOPTIN_CHANNEL",
                    Message = "Re-opt-in channel is invalid.",
                    CorrelationId = correlationId
                });
            }

            return Unauthorized(new ApiErrorResponse
            {
                Code = result.Code,
                Message = "Re-opt-in authorization failed.",
                CorrelationId = correlationId
            });
        }

        return Ok(new ReOptInResponse
        {
            UpdatedConsentStatus = result.UpdatedStatus.ToString().ToUpperInvariant().Replace("OPT", "OPT-"),
            UpdateResult = result.UpdateResult,
            UpdateTimestampUtc = result.UpdateTimestampUtc,
            CorrelationId = correlationId
        });
    }

    private static bool IsValid(ReOptInRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(request.ReOptInRequestId)
            && !string.IsNullOrWhiteSpace(request.CustomerPhoneNumber)
            && request.InitiationChannel is not null
            && request.InitiatedAtUtc is not null;
    }
}
