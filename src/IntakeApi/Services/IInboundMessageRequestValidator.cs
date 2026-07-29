using IntakeApi.Contracts;

namespace IntakeApi.Services;

/// <summary>
/// Validates inbound intake requests.
/// </summary>
public interface IInboundMessageRequestValidator
{
    /// <summary>
    /// Validates an inbound intake request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>The validation result.</returns>
    InboundMessageValidationResult Validate(InboundMessageRequest? request);
}
