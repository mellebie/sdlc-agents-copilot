// Tests for HealthController
// Source: TASK (Data Services) | EPIC-007 (STORY-021) | TASK-059, NFS-001
// Covers: 200 OK when healthy, 503 when degraded, response structure, no information disclosure

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Controllers;
using TCPA.Api.Services.AuditLog;
using Xunit;

namespace TCPA.Api.Tests.Unit.Controllers;

public sealed class HealthControllerTests
{
    private readonly Mock<HealthCheckService> _healthCheckService;
    private readonly Mock<ICorrelationIdAccessor> _correlationIdAccessor;
    private readonly HealthController _sut;

    public HealthControllerTests()
    {
        _healthCheckService = new Mock<HealthCheckService>();
        _correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        _correlationIdAccessor.Setup(a => a.CorrelationId).Returns("test-correlation-id");

        _sut = new HealthController(
            _healthCheckService.Object,
            new Mock<ILogger<HealthController>>().Object,
            _correlationIdAccessor.Object);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task GetHealth_Should_Return200OK_When_AllDependenciesAreHealthy()
    {
        // Arrange
        var healthReport = BuildHealthReport(HealthStatus.Healthy, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Healthy
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var response = okResult.Value.Should().BeOfType<HealthResponse>().Subject;
        response.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task GetHealth_Should_Return503_When_AnyDependencyIsUnhealthy()
    {
        // Arrange — database unhealthy
        var healthReport = BuildHealthReport(HealthStatus.Unhealthy, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Unhealthy
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);

        var response = statusResult.Value.Should().BeOfType<HealthResponse>().Subject;
        response.Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetHealth_Should_Return503_When_DegradedStatus()
    {
        // Arrange — degraded (not healthy) maps to 503
        var healthReport = BuildHealthReport(HealthStatus.Degraded, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Degraded
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert — any non-Healthy status returns 503
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task GetHealth_Should_IncludePerCheckStatus_InResponse()
    {
        // Arrange
        var healthReport = BuildHealthReport(HealthStatus.Healthy, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Healthy,
            ["external-api"] = HealthStatus.Healthy
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert — checks dictionary includes all registered checks
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<HealthResponse>().Subject;

        response.Checks.Should().ContainKey("database");
        response.Checks["database"].Status.Should().Be("ok");
        response.Checks.Should().ContainKey("external-api");
    }

    [Fact]
    public async Task GetHealth_Should_ReturnDegradedStatus_ForFailingChecks()
    {
        // Arrange — one check healthy, one unhealthy
        var healthReport = BuildHealthReport(HealthStatus.Unhealthy, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Unhealthy,
            ["cache"] = HealthStatus.Healthy
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        var response = statusResult.Value.Should().BeOfType<HealthResponse>().Subject;

        response.Checks["database"].Status.Should().Be("degraded");
        response.Checks["cache"].Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_Should_SanitizeConnectionStrings_InDescription()
    {
        // Arrange — description contains a connection string (security risk)
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["database"] = new HealthReportEntry(
                status: HealthStatus.Unhealthy,
                description: "Server=prod-db.example.com;Password=supersecret",
                duration: TimeSpan.Zero,
                exception: null,
                data: null)
        };

        var healthReport = new HealthReport(entries, HealthStatus.Unhealthy);

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert — connection string must NOT appear in the response
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        var response = statusResult.Value.Should().BeOfType<HealthResponse>().Subject;

        var dbCheck = response.Checks["database"];
        dbCheck.Description.Should().NotContain("Server=");
        dbCheck.Description.Should().NotContain("Password=");
        dbCheck.Description.Should().NotContain("prod-db.example.com");
    }

    [Fact]
    public async Task GetHealth_Should_IncludeTimestamp_InResponse()
    {
        // Arrange
        var healthReport = BuildHealthReport(HealthStatus.Healthy, new Dictionary<string, HealthStatus>
        {
            ["database"] = HealthStatus.Healthy
        });

        _healthCheckService
            .Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthReport);

        // Act
        var result = await _sut.GetHealth(CancellationToken.None);

        // Assert — timestamp must be present in ISO 8601 format
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<HealthResponse>().Subject;

        response.Timestamp.Should().NotBeNullOrEmpty();
        // Verify it parses as a valid date
        DateTime.TryParse(response.Timestamp, out _).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static HealthReport BuildHealthReport(
        HealthStatus overallStatus,
        Dictionary<string, HealthStatus> checkStatuses)
    {
        var entries = checkStatuses.ToDictionary(
            kvp => kvp.Key,
            kvp => new HealthReportEntry(
                status: kvp.Value,
                description: kvp.Value == HealthStatus.Healthy ? "OK" : "Check failed",
                duration: TimeSpan.FromMilliseconds(10),
                exception: null,
                data: null));

        return new HealthReport(entries, overallStatus);
    }
}
