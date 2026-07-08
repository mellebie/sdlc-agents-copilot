// tests/TCPA.Api.Tests/Unit/Controllers/AdminControllerTests.cs
// Tests for AdminController — POST /api/v1/admin/reopt-in and GET /api/v1/admin/status/{cell}
// Source: TASK-026, TASK-029 | SPEC-007, SPEC-010 | STORY-009, STORY-010
// Business Rules: BR-031 through BR-038

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Controllers;
using TCPA.Api.Services.ReOptIn;
using Xunit;

namespace TCPA.Api.Tests.Unit.Controllers;

/// <summary>
/// Tests for <see cref="AdminController"/>.
/// Verifies: 200 on success, 404 on unknown, 409 on no-record, 400 on missing fields,
/// 503 on service failure, masked cell number in GET response.
/// </summary>
public sealed class AdminControllerTests
{
    private readonly Mock<IReOptInService> _reOptInServiceMock;
    private readonly Mock<ILogger<AdminController>> _loggerMock;
    private readonly AdminController _sut;

    private const string ValidCellNumber = "+12025551234";
    private const string ValidReason = "Customer called support and confirmed re-opt-in request";
    private const string AgentUserId = "agent@company.com";

    public AdminControllerTests()
    {
        _reOptInServiceMock = new Mock<IReOptInService>();
        _loggerMock = new Mock<ILogger<AdminController>>();
        _sut = new AdminController(_reOptInServiceMock.Object, _loggerMock.Object);

        // Set up a default authenticated user context
        _sut.ControllerContext = BuildControllerContext(AgentUserId);
    }

    // -----------------------------------------------------------------------
    // POST /api/v1/admin/reopt-in — 200 success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return200_When_ReOptInSucceeds()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                ValidCellNumber, It.IsAny<string>(), ValidReason, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessfulReOptInResult("OPT_OUT"));

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert
        actionResult.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Should_Return200_When_NumberIsAlreadyOptIn_IdempotentCase()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                ValidCellNumber, It.IsAny<string>(), ValidReason, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessfulReOptInResult("OPT_IN")); // idempotent

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert — idempotent should also be 200
        actionResult.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Should_ReturnReOptInResponse_When_ReOptInSucceeds()
    {
        // Arrange
        ReOptInResult serviceResult = BuildSuccessfulReOptInResult("OPT_OUT");
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                ValidCellNumber, It.IsAny<string>(), ValidReason, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceResult);

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ReOptInResponse>().Subject;
        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be("OPT_IN");
    }

    // -----------------------------------------------------------------------
    // POST /api/v1/admin/reopt-in — 409 Conflict (no prior opt-out record)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return409_When_NumberHasNoPriorOptOutRecord()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                ValidCellNumber, It.IsAny<string>(), ValidReason, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReOptInResult
            {
                Success = false,
                PreviousStatus = ReOptInService.NoRecordStatus,
                NewStatus = ReOptInService.NoRecordStatus,
                UpdatedTimestamp = DateTime.UtcNow,
                RecordId = null,
                Message = "No opt-out record exists for this number.",
            });

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert — BR-038: 409 Conflict when no prior opt-out record
        var conflictResult = actionResult.Should().BeOfType<ConflictObjectResult>().Subject;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    // -----------------------------------------------------------------------
    // POST /api/v1/admin/reopt-in — 503 on service exception
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return503_When_ServiceThrowsUnexpectedException()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert
        var statusResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    // -----------------------------------------------------------------------
    // POST /api/v1/admin/reopt-in — 400 on ArgumentException from service
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return400_When_ServiceThrowsArgumentException()
    {
        // Arrange — service validates reason length and throws
        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Reason must be at least 20 characters.", "reason"));

        // Act
        IActionResult actionResult = await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert
        var badRequestResult = actionResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/admin/status/{cellPhoneNumber} — 200 with masked number
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return200_When_StatusRecordExists()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.GetStatusAsync(ValidCellNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptOutStatusResult
            {
                MaskedCellNumber = "******1234",
                OptOutStatus = "OPT_OUT",
                LastOptOutTimestamp = DateTime.UtcNow.AddHours(-1),
                LastOptInTimestamp = null,
            });

        // Act
        IActionResult actionResult = await _sut.GetStatus(ValidCellNumber, CancellationToken.None);

        // Assert
        actionResult.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Should_ReturnMaskedCellNumber_When_StatusRecordExists()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.GetStatusAsync(ValidCellNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptOutStatusResult
            {
                MaskedCellNumber = "******1234",
                OptOutStatus = "OPT_OUT",
                LastOptOutTimestamp = DateTime.UtcNow.AddHours(-1),
                LastOptInTimestamp = null,
            });

        // Act
        IActionResult actionResult = await _sut.GetStatus(ValidCellNumber, CancellationToken.None);

        // Assert — full phone number must not appear in response (BR-037)
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<OptOutStatusResponse>().Subject;
        response.MaskedCellNumber.Should().NotBe(ValidCellNumber,
            because: "the full cell number must be masked in the response");
        response.MaskedCellNumber.Should().Be("******1234");
    }

    [Fact]
    public async Task Should_ReturnOptOutStatus_When_StatusRecordExists()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.GetStatusAsync(ValidCellNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptOutStatusResult
            {
                MaskedCellNumber = "******1234",
                OptOutStatus = "OPT_OUT",
                LastOptOutTimestamp = DateTime.UtcNow.AddHours(-1),
                LastOptInTimestamp = null,
            });

        // Act
        IActionResult actionResult = await _sut.GetStatus(ValidCellNumber, CancellationToken.None);

        // Assert
        var response = (actionResult as OkObjectResult)!.Value as OptOutStatusResponse;
        response!.OptOutStatus.Should().Be("OPT_OUT");
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/admin/status/{cellPhoneNumber} — 404 on unknown number
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Return404_When_NoRecordExistsForCellNumber()
    {
        // Arrange
        _reOptInServiceMock
            .Setup(s => s.GetStatusAsync(ValidCellNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OptOutStatusResult?)null);

        // Act
        IActionResult actionResult = await _sut.GetStatus(ValidCellNumber, CancellationToken.None);

        // Assert
        actionResult.Should().BeOfType<NotFoundObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/admin/status/{cellPhoneNumber} — 400 on invalid E.164 format
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("12025551234")]    // missing leading +
    [InlineData("not-a-number")]  // not numeric
    [InlineData("")]              // empty
    [InlineData("+0123456789")]   // starts with +0 (invalid E.164)
    public async Task Should_Return400_When_CellPhoneNumberIsInvalidE164(string invalidNumber)
    {
        // Act
        IActionResult actionResult = await _sut.GetStatus(invalidNumber, CancellationToken.None);

        // Assert
        actionResult.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Should_Return400_When_CellPhoneNumberIsNull()
    {
        // Act
        IActionResult actionResult = await _sut.GetStatus(null!, CancellationToken.None);

        // Assert
        actionResult.Should().BeOfType<BadRequestObjectResult>();
    }

    // -----------------------------------------------------------------------
    // Agent user ID is extracted from JWT — not from request body
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_PassAgentUserIdFromJwtToService_When_ReOptInCalled()
    {
        // Arrange — set up specific identity in controller context
        const string jwtAgentId = "jwt-agent@company.com";
        _sut.ControllerContext = BuildControllerContext(jwtAgentId);

        _reOptInServiceMock
            .Setup(s => s.ReOptInAsync(
                ValidCellNumber, jwtAgentId, ValidReason, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessfulReOptInResult("OPT_OUT"));

        // Act
        await _sut.ReOptIn(
            new ReOptInRequest { CellPhoneNumber = ValidCellNumber, Reason = ValidReason },
            CancellationToken.None);

        // Assert — service called with agent ID from token, not from request body (TASK-029)
        _reOptInServiceMock.Verify(
            s => s.ReOptInAsync(
                ValidCellNumber, jwtAgentId, ValidReason, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ReOptInResult BuildSuccessfulReOptInResult(string previousStatus) =>
        new ReOptInResult
        {
            Success = true,
            PreviousStatus = previousStatus,
            NewStatus = "OPT_IN",
            UpdatedTimestamp = DateTime.UtcNow,
            RecordId = Guid.NewGuid(),
            Message = "Number successfully re-opted-in.",
        };

    private static ControllerContext BuildControllerContext(string userName)
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName),
            new System.Security.Claims.Claim("sub", userName),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal,
            },
        };
    }
}
