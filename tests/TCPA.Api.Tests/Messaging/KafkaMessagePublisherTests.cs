// Tests: KafkaMessagePublisher — construction and health-check behaviour
// Source: Task 5 | IMessagePublisher contract

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TCPA.Api.Messaging;
using Xunit;

namespace TCPA.Api.Tests.Messaging;

public class KafkaMessagePublisherTests
{
    [Fact]
    public void Constructor_MissingBootstrapServers_DoesNotThrow()
    {
        // Arrange — empty config: no Kafka keys present
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var logger = Substitute.For<ILogger<KafkaMessagePublisher>>();

        // Act + Assert — must fall back to localhost:9092, not throw
        var act = () => new KafkaMessagePublisher(config, logger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task CheckHealthAsync_KafkaUnreachable_ReturnsFalse()
    {
        // Arrange — point at a port with nothing listening
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:19999",
                ["Kafka:Topics:Inbound"]   = "inbound-messages",
                ["Kafka:Topics:Outbound"]  = "outbound-messages"
            })
            .Build();
        var logger = Substitute.For<ILogger<KafkaMessagePublisher>>();
        var sut = new KafkaMessagePublisher(config, logger);

        // Act
        var result = await sut.CheckHealthAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
}
