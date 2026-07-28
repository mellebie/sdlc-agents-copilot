using IntakeApi.Contracts;
using IntakeApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace IntakeApi.Controllers;

/// <summary>
/// Accepts inbound text message events for the compliance intake pipeline.
/// </summary>
[Route("api/v1/inbound/messages")]
[Produces("application/json")]
public sealed class InboundMessagesController : ControllerBase
{
    private const string AcceptedClassificationState = "PENDING";
    private readonly ICorrelationIdGenerator _correlationIdGenerator;
    private readonly IRoutingEligibilityService _routingEligibilityService;
    private readonly IInboundMessageRequestValidator _requestValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboundMessagesController"/> class.
    /// </summary>
    /// <param name="correlationIdGenerator">Correlation identifier generator.</param>
    /// <param name="requestValidator">Inbound request validator.</param>
    public InboundMessagesController(
        ICorrelationIdGenerator correlationIdGenerator,
        IRoutingEligibilityService routingEligibilityService,
        IInboundMessageRequestValidator requestValidator)
    {
        _correlationIdGenerator = correlationIdGenerator;
        _routingEligibilityService = routingEligibilityService;
        _requestValidator = requestValidator;
    }

    /// <summary>
    /// Accepts a validated inbound message request and returns the intake acknowledgement.
    /// </summary>
    /// <param name="request">Inbound message intake request.</param>
    /// <returns>An accepted response when the request is valid; otherwise a structured validation error.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(InboundMessageAcceptedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public ActionResult<InboundMessageAcceptedResponse> Post([FromBody] InboundMessageRequest? request)
    {
        var correlationId = _correlationIdGenerator.Generate();
        var validationResult = _requestValidator.Validate(request);

        if (!validationResult.IsValid)
        {
            return BadRequest(CreateErrorResponse(correlationId, validationResult));
        }

        var routingResult = _routingEligibilityService.Evaluate(request!);
        if (!routingResult.IsEligible)
        {
            return NotFound(new ApiErrorResponse
            {
                Code = "SCOPE_MAPPING_NOT_FOUND",
                Message = $"Inbound request is out of scope for current mapping set ({routingResult.MappingVersion}). Reason: {routingResult.ReasonCode}.",
                CorrelationId = correlationId
            });
        }

        var response = new InboundMessageAcceptedResponse
        {
            Accepted = true,
            ClassificationState = AcceptedClassificationState,
            CorrelationId = correlationId
        };

        return Ok(response);
    }

    private static ApiErrorResponse CreateErrorResponse(string correlationId, InboundMessageValidationResult validationResult)
    {
        var message = string.Join(" ", validationResult.Failures.Select(failure => failure.Message));
        return new ApiErrorResponse
        {
            Code = "INVALID_INPUT",
            Message = message,
            CorrelationId = correlationId
        };
    }
}
