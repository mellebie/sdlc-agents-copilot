// Tests: KafkaMessagePublisher — construction and health-check behaviour
// Source: Task 5 | IMessagePublisher contract

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TCPA.Api.Messaging;
using TCPA.Core.Services;
using Xunit;

namespace TCPA.Api.Tests.Messaging;

public class KafkaMessagePublisherTests
{
    /// <summary>Returns a hasher substitute that echoes a fixed hash string for any input.</summary>
    private static IPhoneNumberHasher BuildHasher()
    {
        var hasher = Substitute.For<IPhoneNumberHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hashed");
        return hasher;
    }

    [Fact]
    public void Constructor_MissingBootstrapServers_DoesNotThrow()
    {
        // Arrange — empty config: no Kafka keys present
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var logger = Substitute.For<ILogger<KafkaMessagePublisher>>();
        var hasher = BuildHasher();

        // Act + Assert — must fall back to localhost:9092, not throw
        var act = () => new KafkaMessagePublisher(config, logger, hasher);
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
        var sut = new KafkaMessagePublisher(config, logger, BuildHasher());

        // Act
        var result = await sut.CheckHealthAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
}
