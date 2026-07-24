// HealthControllerTests.cs
// Source: Task 10 | SPEC-health | AC: healthy/degraded responses
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TCPA.Api.Controllers;
using TCPA.Api.Messaging;
using TCPA.Api.Models;
using Xunit;

namespace TCPA.Api.Tests.Controllers;

public class HealthControllerTests
{
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly ILogger<HealthController> _logger = Substitute.For<ILogger<HealthController>>();

    [Fact]
    public async Task GetHealth_AllHealthy_Returns200()
    {
        var sut = new HealthController(_publisher, _logger);

        var result = await sut.GetHealthAsync_ForTesting(kafkaOk: true, dbOk: true);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<HealthResponse>().Subject;
        response.Status.Should().Be("healthy");
        response.Checks.Database.Should().Be("ok");
        response.Checks.Kafka.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_KafkaDegraded_Returns503()
    {
        var sut = new HealthController(_publisher, _logger);

        var result = await sut.GetHealthAsync_ForTesting(kafkaOk: false, dbOk: true);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        var response = ((ObjectResult)result).Value.Should().BeOfType<HealthResponse>().Subject;
        response.Status.Should().Be("degraded");
        response.Checks.Kafka.Should().Be("degraded");
        response.Checks.Database.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_DbDegraded_Returns503()
    {
        var sut = new HealthController(_publisher, _logger);

        var result = await sut.GetHealthAsync_ForTesting(kafkaOk: true, dbOk: false);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        var response = ((ObjectResult)result).Value.Should().BeOfType<HealthResponse>().Subject;
        response.Status.Should().Be("degraded");
        response.Checks.Database.Should().Be("degraded");
        response.Checks.Kafka.Should().Be("ok");
    }
}
