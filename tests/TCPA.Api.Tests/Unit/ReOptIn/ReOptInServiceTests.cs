// tests/TCPA.Api.Tests/Unit/ReOptIn/ReOptInServiceTests.cs
// Tests for ReOptInService — admin re-opt-in workflow
// Source: TASK-028, TASK-029, TASK-038 | SPEC-007, SPEC-010 | STORY-009, STORY-010, STORY-013
// Business Rules: BR-031 through BR-038, BR-049, BR-050

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.ReOptIn;
using Xunit;

namespace TCPA.Api.Tests.Unit.ReOptIn;

/// <summary>
/// Tests for <see cref="ReOptInService"/>.
/// Verifies: successful re-opt-in, idempotent OPT_IN case, no-record 409 path,
/// audit log written, audit log failure does NOT roll back status change.
/// </summary>
public sealed class ReOptInServiceTests : IDisposable
{
    private readonly TcpaDbContext _dbContext;
    private readonly Mock<IAuditLogService> _auditLogMock;
    private readonly Mock<ILogger<ReOptInService>> _loggerMock;
    private readonly ReOptInService _sut;

    private const string ValidCellNumber = "+12025551234";
    private const string ValidAgentId = "agent@company.com";
    private const string ValidReason = "Customer called support and confirmed re-opt-in request"; // >= 20 chars

    public ReOptInServiceTests()
    {
        DbContextOptions<TcpaDbContext> options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TcpaDbContext(options);
        _auditLogMock = new Mock<IAuditLogService>();
        _loggerMock = new Mock<ILogger<ReOptInService>>();
        _sut = new ReOptInService(_dbContext, _auditLogMock.Object, _loggerMock.Object);

        // Default: audit log write always succeeds
        _auditLogMock
            .Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // Helper to seed an OPT-OUT record
    private async Task<CellNumberOptOutRecord> SeedOptOutRecordAsync(
        string cellNumber, OptOutStatus status = OptOutStatus.OptOut)
    {
        var record = new CellNumberOptOutRecord
        {
            Id = Guid.NewGuid(),
            CellPhoneNumber = cellNumber,
            Status = status,
            LastOptOutTimestamp = status == OptOutStatus.OptOut ? DateTime.UtcNow.AddHours(-1) : null,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        };
        _dbContext.OptOutRecords.Add(record);
        await _dbContext.SaveChangesAsync();
        return record;
    }

    // -----------------------------------------------------------------------
    // ReOptInAsync — successful re-opt-in (OPT_OUT → OPT_IN)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_SetStatusToOptIn_When_NumberWasOptOut()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act
        ReOptInResult result = await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert
        result.Success.Should().BeTrue();
        result.PreviousStatus.Should().Be("OPT_OUT");
        result.NewStatus.Should().Be("OPT_IN");
        result.RecordId.Should().NotBeNull();

        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == ValidCellNumber);
        record!.Status.Should().Be(OptOutStatus.OptIn);
    }

    [Fact]
    public async Task Should_WriteAuditLog_When_ReOptInSucceeds()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act
        await _sut.ReOptInAsync(ValidCellNumber, ValidAgentId, ValidReason, ticketReference: "TICKET-001");

        // Assert
        _auditLogMock.Verify(
            a => a.LogAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EventType == AuditEventType.ReOptIn &&
                    e.AgentUserId == ValidAgentId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // ReOptInAsync — idempotent: already OPT_IN returns success without DB write
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnSuccess_When_NumberIsAlreadyOptIn()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptIn);

        // Act
        ReOptInResult result = await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert — idempotent success per BR-035
        result.Success.Should().BeTrue();
        result.PreviousStatus.Should().Be("OPT_IN");
        result.NewStatus.Should().Be("OPT_IN");
    }

    [Fact]
    public async Task Should_NotChangeDbRecord_When_NumberIsAlreadyOptIn()
    {
        // Arrange
        CellNumberOptOutRecord original = await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptIn);
        DateTime originalUpdatedAt = original.UpdatedAt;

        // Act
        await _sut.ReOptInAsync(ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert — record was not updated
        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == ValidCellNumber);
        record!.Status.Should().Be(OptOutStatus.OptIn);
        record.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public async Task Should_WriteAuditLog_When_NumberIsAlreadyOptIn()
    {
        // Arrange — even idempotent calls must be audited (BR-035)
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptIn);

        // Act
        await _sut.ReOptInAsync(ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert
        _auditLogMock.Verify(
            a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // ReOptInAsync — no record at all → returns NoRecordStatus (controller maps to 409)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnNoRecordStatus_When_NumberHasNoPriorOptOutRecord()
    {
        // Arrange — no record seeded for this number
        // Act
        ReOptInResult result = await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert — sentinel so controller returns 409 (BR-038)
        result.Success.Should().BeFalse();
        result.PreviousStatus.Should().Be(ReOptInService.NoRecordStatus);
        result.NewStatus.Should().Be(ReOptInService.NoRecordStatus);
    }

    [Fact]
    public async Task Should_NotWriteAuditLog_When_NumberHasNoPriorOptOutRecord()
    {
        // Act
        await _sut.ReOptInAsync(ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert — no audit entry for rejected re-opt-in
        _auditLogMock.Verify(
            a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // ReOptInAsync — audit log failure does NOT roll back status change
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_KeepStatusChange_When_AuditLogWriteFails()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Audit log throws — simulates DB failure on audit write
        _auditLogMock
            .Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuditLogWriteException("ReOptIn", "corr-001", "DB unavailable"));

        // Act — must NOT throw (NFS-008 principle: status change committed, audit is best-effort)
        Func<Task> act = async () => await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        await act.Should().NotThrowAsync(
            because: "audit log failure must be logged as critical but must not reverse the status change");

        // Assert — status change was persisted despite audit failure
        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == ValidCellNumber);
        record!.Status.Should().Be(OptOutStatus.OptIn,
            because: "the status change must survive even if the audit write fails");
    }

    // -----------------------------------------------------------------------
    // GetStatusAsync — returns masked cell number and correct status
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnStatusResult_When_RecordExists()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act
        OptOutStatusResult? result = await _sut.GetStatusAsync(ValidCellNumber);

        // Assert
        result.Should().NotBeNull();
        result!.OptOutStatus.Should().Be("OPT_OUT");
    }

    [Fact]
    public async Task Should_ReturnMaskedCellNumber_When_RecordExists()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act
        OptOutStatusResult? result = await _sut.GetStatusAsync(ValidCellNumber);

        // Assert — full number must NOT appear; last 4 digits only (BR-037)
        result!.MaskedCellNumber.Should().NotContain(ValidCellNumber,
            because: "the full cell number must never be returned to the caller");
        result.MaskedCellNumber.Should().EndWith("1234");
    }

    [Fact]
    public async Task Should_ReturnNull_When_NoRecordExistsForCellNumber()
    {
        // Arrange — no record seeded
        // Act
        OptOutStatusResult? result = await _sut.GetStatusAsync(ValidCellNumber);

        // Assert — null signals HTTP 404
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ThrowArgumentException_When_CellPhoneNumberIsWhiteSpace()
    {
        // Act
        Func<Task> act = async () => await _sut.ReOptInAsync(
            "   ", ValidAgentId, ValidReason, ticketReference: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cellPhoneNumber*");
    }

    [Fact]
    public async Task Should_ThrowArgumentException_When_RequestedByIsWhiteSpace()
    {
        // Act
        Func<Task> act = async () => await _sut.ReOptInAsync(
            ValidCellNumber, "   ", ValidReason, ticketReference: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*requestedBy*");
    }

    [Fact]
    public async Task Should_ThrowArgumentException_When_ReasonIsShorterThan20Characters()
    {
        // Act
        Func<Task> act = async () => await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, "Too short", ticketReference: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*20 characters*");
    }

    [Fact]
    public async Task Should_ThrowArgumentException_When_ReasonIsWhiteSpace()
    {
        // Act
        Func<Task> act = async () => await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, "   ", ticketReference: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*reason*");
    }

    // -----------------------------------------------------------------------
    // Ticket reference is optional
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Succeed_When_TicketReferenceIsNull()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act — ticketReference is optional per SPEC-007
        ReOptInResult result = await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: null);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Succeed_When_TicketReferenceIsProvided()
    {
        // Arrange
        await SeedOptOutRecordAsync(ValidCellNumber, OptOutStatus.OptOut);

        // Act
        ReOptInResult result = await _sut.ReOptInAsync(
            ValidCellNumber, ValidAgentId, ValidReason, ticketReference: "HD-12345");

        // Assert
        result.Success.Should().BeTrue();
    }
}
