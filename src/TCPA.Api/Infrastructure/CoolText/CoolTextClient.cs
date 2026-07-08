using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TCPA.Api.Models;

namespace TCPA.Api.Infrastructure.CoolText;

/// <summary>
/// HTTP client implementation for the Cool Text SMS platform.
///
/// Implements two interfaces:
/// <list type="bullet">
///   <item>
///     <see cref="ICoolTextClient"/> — outbound SMS sending (SPEC-001).
///   </item>
///   <item>
///     <see cref="ICoolTextForwardingClient"/> — inbound SMS forwarding to SCG application
///     callback URLs with retry/backoff (SPEC-002).
///   </item>
/// </list>
///
/// All cell phone numbers are PII and must be logged as last 4 digits only (BR-068).
/// </summary>
public sealed class CoolTextClient : ICoolTextClient, ICoolTextForwardingClient
{
    private const int MaxForwardingAttempts = 3;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoolTextClient> _logger;

    /// <summary>
    /// Initializes the Cool Text client with an <see cref="HttpClient"/> pre-configured
    /// by <c>IHttpClientFactory</c> (base address, default headers, timeout).
    /// </summary>
    /// <param name="httpClient">Named/typed HTTP client for the Cool Text API.</param>
    /// <param name="logger">Structured logger.</param>
    public CoolTextClient(HttpClient httpClient, ILogger<CoolTextClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---------------------------------------------------------------------------
    // ICoolTextClient — outbound SMS sending
    // ---------------------------------------------------------------------------

    /// <summary>
    /// 3-argument overload: sends an outbound SMS, returning the platform message ID string.
    /// Used by <see cref="Services.OptOut.ConfirmationDispatcher"/> for confirmation messages.
    /// Delegates to the cancellable 4-argument overload.
    /// </summary>
    public async Task<string> SendSmsAsync(
        string fromAccountId,
        string toPhoneNumber,
        string messageBody)
    {
        SendSmsResult result = await SendSmsAsync(fromAccountId, toPhoneNumber, messageBody, CancellationToken.None);
        return result.MessageId;
    }

    // ---------------------------------------------------------------------------
    // SendSmsAsync (4-argument) — outbound SMS with CancellationToken
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Sends an outbound SMS via the Cool Text platform. Called only after the compliance gate
    /// has confirmed OPT_IN status for the destination number (SPEC-001).
    /// </summary>
    /// <param name="fromAccountId">Cool Text account to send from.</param>
    /// <param name="toPhoneNumber">Destination E.164 number (PII — logged as last 4 digits).</param>
    /// <param name="messageBody">SMS body content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="SendSmsResult"/> containing the Cool Text platform message ID and delivery status.
    /// </returns>
    /// <exception cref="CoolTextApiException">Thrown on HTTP error or unexpected API response.</exception>
    public async Task<SendSmsResult> SendSmsAsync(
        string fromAccountId,
        string toPhoneNumber,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPhoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageBody);

        var maskedNumber = MaskCellNumber(toPhoneNumber);

        _logger.LogInformation(
            "Sending outbound SMS via Cool Text. Account: {AccountId}. Destination: ****{MaskedNumber}.",
            fromAccountId, maskedNumber);

        var sendRequest = new CoolTextSendRequest
        {
            AccountId = fromAccountId,
            To = toPhoneNumber,
            Body = messageBody
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/v1/messages", sendRequest, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new CoolTextApiException(
                $"HTTP error calling Cool Text SendSms for destination ****{maskedNumber}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CoolTextApiException(
                $"Timeout calling Cool Text SendSms for destination ****{maskedNumber}.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CoolTextApiException(
                $"Cool Text returned HTTP {(int)response.StatusCode} for destination ****{maskedNumber}.",
                (int)response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<CoolTextSendApiResponse>(
            JsonOptions, cancellationToken);

        if (result is null || string.IsNullOrWhiteSpace(result.MessageId))
        {
            throw new CoolTextApiException(
                $"Cool Text returned success but no message_id for destination ****{maskedNumber}.");
        }

        _logger.LogInformation(
            "Outbound SMS accepted by Cool Text. Account: {AccountId}. Destination: ****{MaskedNumber}. " +
            "MessageId: {MessageId}.",
            fromAccountId, maskedNumber, result.MessageId);

        return new SendSmsResult
        {
            MessageId = result.MessageId,
            Status = result.Status ?? "queued"
        };
    }

    // ---------------------------------------------------------------------------
    // ICoolTextForwardingClient — inbound SMS forwarding
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Forwards an inbound SMS message to the registered SCG application's callback URL.
    /// Retries up to 3 times with exponential backoff (1s, 2s, 4s) on transient HTTP errors
    /// or non-2xx responses per SPEC-002.
    /// After all retries are exhausted, throws <see cref="CoolTextForwardingException"/>;
    /// the caller logs the permanent failure and takes no further action.
    /// </summary>
    /// <param name="applicationWebhookUrl">HTTPS callback URL for the SCG application.</param>
    /// <param name="message">Inbound SMS message to forward.</param>
    /// <exception cref="CoolTextForwardingException">Thrown when all retry attempts are exhausted.</exception>
    public async Task ForwardToApplicationAsync(string applicationWebhookUrl, InboundSmsMessage message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWebhookUrl);
        ArgumentNullException.ThrowIfNull(message);

        var maskedNumber = MaskCellNumber(message.SenderCellNumber);

        var callbackPayload = new ApplicationCallbackPayload
        {
            SenderCellNumber = message.SenderCellNumber,
            MessageBody = message.MessageBody,
            CoolTextAccountId = message.CoolTextAccountId,
            ReceivedTimestamp = DateTimeOffset.UtcNow
        };

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxForwardingAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Forwarding inbound SMS from ****{MaskedNumber} to application callback. " +
                    "Attempt {Attempt}/{Max}.",
                    maskedNumber, attempt, MaxForwardingAttempts);

                using var response = await _httpClient.PostAsJsonAsync(
                    applicationWebhookUrl, callbackPayload, JsonOptions);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Inbound SMS from ****{MaskedNumber} successfully forwarded to application " +
                        "on attempt {Attempt}.",
                        maskedNumber, attempt);
                    return;
                }

                _logger.LogWarning(
                    "Application callback returned HTTP {StatusCode} for ****{MaskedNumber}. " +
                    "Attempt {Attempt}/{Max}.",
                    (int)response.StatusCode, maskedNumber, attempt, MaxForwardingAttempts);

                lastException = new CoolTextForwardingException(applicationWebhookUrl, attempt);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "HTTP error forwarding inbound SMS from ****{MaskedNumber}. Attempt {Attempt}/{Max}.",
                    maskedNumber, attempt, MaxForwardingAttempts);
                lastException = ex;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex,
                    "Timeout forwarding inbound SMS from ****{MaskedNumber}. Attempt {Attempt}/{Max}.",
                    maskedNumber, attempt, MaxForwardingAttempts);
                lastException = ex;
            }

            if (attempt < MaxForwardingAttempts)
            {
                await Task.Delay(RetryDelays[attempt - 1]);
            }
        }

        _logger.LogError(
            "Permanent delivery failure: inbound SMS from ****{MaskedNumber} could not be forwarded " +
            "to {Url} after {Max} attempts.",
            maskedNumber, applicationWebhookUrl, MaxForwardingAttempts);

        throw new CoolTextForwardingException(applicationWebhookUrl, MaxForwardingAttempts, lastException);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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

    // ---------------------------------------------------------------------------
    // Private DTOs — Cool Text API communication only
    // ---------------------------------------------------------------------------

    private sealed class CoolTextSendRequest
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; init; } = string.Empty;

        [JsonPropertyName("to")]
        public string To { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;
    }

    private sealed class CoolTextSendApiResponse
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class ApplicationCallbackPayload
    {
        [JsonPropertyName("sender_cell_number")]
        public string SenderCellNumber { get; init; } = string.Empty;

        [JsonPropertyName("message_body")]
        public string MessageBody { get; init; } = string.Empty;

        [JsonPropertyName("cool_text_account_id")]
        public string CoolTextAccountId { get; init; } = string.Empty;

        [JsonPropertyName("received_timestamp")]
        public DateTimeOffset ReceivedTimestamp { get; init; }
    }
}
