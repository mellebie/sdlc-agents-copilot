using IntentClassifier.Services;

namespace IntentClassifier.Handlers;

public interface INonStopForwardingHandler
{
    Task<NonStopForwardingResult> HandleAsync(NonStopForwardingRequest request, CancellationToken cancellationToken = default);
}

public interface IApplicationCallbackClient
{
    Task<ApplicationCallbackResult> ForwardAsync(NonStopForwardingRequest request, CancellationToken cancellationToken = default);
}

public interface IForwardingOutcomeRepository
{
    Task SaveAsync(NonStopForwardingOutcomeRecord record, CancellationToken cancellationToken = default);
}

public sealed class NonStopForwardingHandler : INonStopForwardingHandler
{
    private readonly IApplicationCallbackClient _applicationCallbackClient;
    private readonly IForwardingOutcomeRepository _forwardingOutcomeRepository;

    public NonStopForwardingHandler(
        IApplicationCallbackClient applicationCallbackClient,
        IForwardingOutcomeRepository forwardingOutcomeRepository)
    {
        _applicationCallbackClient = applicationCallbackClient;
        _forwardingOutcomeRepository = forwardingOutcomeRepository;
    }

    public async Task<NonStopForwardingResult> HandleAsync(NonStopForwardingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Intent is not (NormalizedIntent.Help or NormalizedIntent.Other))
        {
            var unsupported = new NonStopForwardingResult(
                false,
                false,
                "UNSUPPORTED_INTENT",
                request.ConsentStatusBeforeHandling,
                "Non-stop forwarding supports HELP/OTHER intents only.");

            await _forwardingOutcomeRepository.SaveAsync(
                new NonStopForwardingOutcomeRecord(request.EventId, request.Intent, false, false, unsupported.Code, unsupported.Message, DateTimeOffset.UtcNow),
                cancellationToken);

            return unsupported;
        }

        try
        {
            var callbackResult = await _applicationCallbackClient.ForwardAsync(request, cancellationToken);
            var result = callbackResult.Success
                ? new NonStopForwardingResult(true, false, "FORWARDED", request.ConsentStatusBeforeHandling, "Forwarded successfully.")
                : new NonStopForwardingResult(false, true, "FORWARDING_FAILED_RETRYABLE", request.ConsentStatusBeforeHandling, callbackResult.Message);

            await _forwardingOutcomeRepository.SaveAsync(
                new NonStopForwardingOutcomeRecord(request.EventId, request.Intent, result.Success, result.Retryable, result.Code, result.Message, DateTimeOffset.UtcNow),
                cancellationToken);

            return result;
        }
        catch (ApplicationEndpointUnavailableException ex)
        {
            var retryable = new NonStopForwardingResult(false, true, "APP_ENDPOINT_UNAVAILABLE", request.ConsentStatusBeforeHandling, ex.Message);
            await _forwardingOutcomeRepository.SaveAsync(
                new NonStopForwardingOutcomeRecord(request.EventId, request.Intent, false, true, retryable.Code, retryable.Message, DateTimeOffset.UtcNow),
                cancellationToken);

            return retryable;
        }
    }
}

public readonly record struct NonStopForwardingRequest(
    string EventId,
    string SourceApplication,
    string CustomerPhoneNumber,
    string MessageText,
    NormalizedIntent Intent,
    ConsentStatus ConsentStatusBeforeHandling);

public readonly record struct NonStopForwardingResult(
    bool Success,
    bool Retryable,
    string Code,
    ConsentStatus ConsentStatusAfterHandling,
    string Message);

public readonly record struct NonStopForwardingOutcomeRecord(
    string EventId,
    NormalizedIntent Intent,
    bool Success,
    bool Retryable,
    string Code,
    string Message,
    DateTimeOffset LoggedAtUtc);

public readonly record struct ApplicationCallbackResult(bool Success, string Message);

public enum ConsentStatus
{
    Unknown = 0,
    OptIn = 1,
    OptOut = 2
}

public sealed class ApplicationEndpointUnavailableException : Exception
{
    public ApplicationEndpointUnavailableException(string message)
        : base(message)
    {
    }
}
