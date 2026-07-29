namespace IntakeApi.Contracts;

/// <summary>
/// Represents the accepted response returned for a valid inbound message.
/// </summary>
public sealed class InboundMessageAcceptedResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the request was accepted.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// Gets or sets the current classification state.
    /// </summary>
    public string ClassificationState { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the generated correlation identifier.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Represents a structured API error response.
/// </summary>
public sealed class ApiErrorResponse
{
    /// <summary>
    /// Gets or sets the stable error code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the request correlation identifier.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Describes a validation issue for a request field.
/// </summary>
public sealed class InboundMessageValidationFailure
{
    /// <summary>
    /// Gets or sets the field that failed validation.
    /// </summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable failure code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the failure message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Represents the result of validating an inbound message request.
/// </summary>
public sealed class InboundMessageValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboundMessageValidationResult"/> class.
    /// </summary>
    /// <param name="failures">Validation failures.</param>
    public InboundMessageValidationResult(IReadOnlyCollection<InboundMessageValidationFailure> failures)
    {
        Failures = failures;
    }

    /// <summary>
    /// Gets the validation failures.
    /// </summary>
    public IReadOnlyCollection<InboundMessageValidationFailure> Failures { get; }

    /// <summary>
    /// Gets a value indicating whether the request is valid.
    /// </summary>
    public bool IsValid => Failures.Count == 0;
}
