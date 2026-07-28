using IntakeApi.Contracts;
using IntakeApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace IntakeApi.Controllers;

[Route("api/v1/enforcement/decisions")]
[Produces("application/json")]
public sealed class EnforcementDecisionsController : ControllerBase
{
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly ICorrelationIdGenerator _correlationIdGenerator;

    public EnforcementDecisionsController(IPolicyEvaluationService policyEvaluationService, ICorrelationIdGenerator correlationIdGenerator)
    {
        _policyEvaluationService = policyEvaluationService;
        _correlationIdGenerator = correlationIdGenerator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(EnforcementDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<EnforcementDecisionResponse>> Post([FromBody] EnforcementDecisionRequest? request, CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdGenerator.Generate();
        if (!IsValid(request))
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "INVALID_OUTBOUND_REQUEST",
                Message = "Outbound request payload is invalid.",
                CorrelationId = correlationId
            });
        }

        var evaluation = await _policyEvaluationService.EvaluateAsync(request!, cancellationToken);
        if (evaluation.OutOfScope)
        {
            return NotFound(new ApiErrorResponse
            {
                Code = "OUT_OF_SCOPE",
                Message = "Outbound request is not in scope.",
                CorrelationId = correlationId
            });
        }

        if (evaluation.GuardedFailure)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "ENFORCEMENT_UNAVAILABLE",
                Message = "Enforcement decision is temporarily unavailable.",
                CorrelationId = correlationId
            });
        }

        return Ok(new EnforcementDecisionResponse
        {
            EnforcementDecision = evaluation.Decision,
            DecisionReason = evaluation.Reason,
            DecisionTimestampUtc = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        });
    }

    private static bool IsValid(EnforcementDecisionRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(request.OutboundRequestId)
            && !string.IsNullOrWhiteSpace(request.CustomerPhoneNumber)
            && request.SourceApplication is not null
            && request.SourceLdc is not null;
    }
}
