using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using TCPA.Core.Interfaces;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Messaging;

namespace TCPA.MessageProcessor.Services;

public class ReplyForwardingService : IReplyForwardingService
{
    private readonly HttpClient _httpClient;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<ReplyForwardingService> _logger;

    public ReplyForwardingService(
        HttpClient httpClient,
        IPhoneNumberHasher hasher,
        ILogger<ReplyForwardingService> logger)
    {
        _httpClient = httpClient;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task ForwardReplyAsync(InboundMessageEvent @event, string callbackUrl, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(@event.Body, System.Text.Encoding.UTF8, "text/plain");
            using var response = await _httpClient.PostAsync(callbackUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Reply forward failed: HTTP {StatusCode} from {CallbackUrl}. ApplicationId={ApplicationId} MessageId={MessageId} PhoneHash={PhoneHash}",
                    (int)response.StatusCode,
                    callbackUrl,
                    @event.ApplicationId,
                    @event.MessageId,
                    _hasher.Hash(@event.From));
            }
            else
            {
                _logger.LogInformation(
                    "Reply forwarded. ApplicationId={ApplicationId} MessageId={MessageId}",
                    @event.ApplicationId, @event.MessageId);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // propagate graceful shutdown — the worker handles this
        }
        catch (Exception ex)
        {
            // BR-017: best-effort, never throw. Swallow so the Kafka offset is committed.
            _logger.LogWarning(ex,
                "Reply forward threw: {CallbackUrl}. ApplicationId={ApplicationId} MessageId={MessageId} PhoneHash={PhoneHash}",
                callbackUrl,
                @event.ApplicationId,
                @event.MessageId,
                _hasher.Hash(@event.From));
        }
    }
}
