using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.OutboundDispatcher.Infrastructure;

/// <summary>
/// Typed HttpClient implementation of <see cref="ICoolTextApiClient"/> for the outbound dispatcher.
/// Posts SMS send requests to the Cool Text gateway and maps HTTP responses to
/// <see cref="CoolTextSendResult"/>. Network errors propagate to the caller — the caller
/// (OutboundSendService) implements the retry loop.
/// </summary>
public class CoolTextApiClient : ICoolTextApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<CoolTextApiClient> _logger;

    /// <summary>Initializes a new instance of <see cref="CoolTextApiClient"/>.</summary>
    public CoolTextApiClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CoolTextApiClient> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["CoolText:ApiKey"]
            ?? throw new InvalidOperationException("CoolText:ApiKey is not configured.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CoolTextSendResult> SendSmsAsync(
        string toPhoneNumber,
        string fromAccountNumber,
        string body,
        CancellationToken ct)
    {
        var payload = new
        {
            to = toPhoneNumber,
            from = fromAccountNumber,
            body = body
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Api-Key", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cool Text API network error for account {AccountNumber}", fromAccountNumber);
            throw;
        }

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CoolTextApiResponse>(cancellationToken: ct);
            return new CoolTextSendResult(true, result?.MessageId, null);
        }

        var errorBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Cool Text API returned {StatusCode} for account {AccountNumber}: {ErrorBody}",
            (int)response.StatusCode, fromAccountNumber, errorBody);
        return new CoolTextSendResult(false, null, $"HTTP {(int)response.StatusCode}: {errorBody}");
    }

    private sealed record CoolTextApiResponse(
        [property: JsonPropertyName("messageId")] string? MessageId,
        [property: JsonPropertyName("status")] string? Status);
}
