using Confluent.Kafka;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Messaging;

/// <summary>
/// Production Kafka implementation of <see cref="IMessagePublisher"/>.
/// Reads broker and topic configuration from <c>IConfiguration</c> at construction time.
/// Registered as a singleton; call <see cref="Dispose"/> on application shutdown.
/// </summary>
public sealed class KafkaMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaMessagePublisher> _logger;
    private readonly string _inboundTopic;
    private readonly string _outboundTopic;
    private readonly string _bootstrapServers;

    /// <summary>
    /// Initialises the publisher. Falls back to <c>localhost:9092</c> / default topic names
    /// when the corresponding configuration keys are absent.
    /// </summary>
    /// <param name="configuration">Application configuration — reads
    /// <c>Kafka:BootstrapServers</c>, <c>Kafka:Topics:Inbound</c>, <c>Kafka:Topics:Outbound</c>.</param>
    /// <param name="logger">Logger for Kafka delivery and health check events.</param>
    public KafkaMessagePublisher(IConfiguration configuration, ILogger<KafkaMessagePublisher> logger)
    {
        _logger = logger;
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _inboundTopic     = configuration["Kafka:Topics:Inbound"]   ?? "inbound-messages";
        _outboundTopic    = configuration["Kafka:Topics:Outbound"]  ?? "outbound-messages";

        _producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = _bootstrapServers })
            .Build();
    }

    /// <inheritdoc />
    public async Task PublishInboundAsync(InboundMessageEvent @event, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            Key   = @event.From,                      // partition by customer phone number
            Value = JsonSerializer.Serialize(@event)
        };

        try
        {
            await _producer.ProduceAsync(_inboundTopic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Kafka delivery failed for inbound message: topic={Topic} key={Key} errorCode={ErrorCode}",
                _inboundTopic, @event.From, ex.Error.Code);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PublishOutboundAsync(OutboundMessageEvent @event, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            Key   = @event.ToNumber,                  // partition by destination phone number
            Value = JsonSerializer.Serialize(@event)
        };

        try
        {
            await _producer.ProduceAsync(_outboundTopic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Kafka delivery failed for outbound message: topic={Topic} key={Key} errorCode={ErrorCode}",
                _outboundTopic, @event.ToNumber, ex.Error.Code);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            using var admin = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
            _ = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kafka health check failed (bootstrap={BootstrapServers})", _bootstrapServers);
            return Task.FromResult(false);
        }
    }

    /// <summary>Disposes the underlying Kafka producer.</summary>
    public void Dispose() => _producer.Dispose();
}
