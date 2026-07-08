// tests/TCPA.Api.Tests/Unit/OptOut/OptOutStatusServiceTests.cs
// Tests for OptOutStatusService — OPT-OUT status read/write
// Source: TASK-018, TASK-019 | SPEC-004 | STORY-005
// Business Rules: BR-016 through BR-020

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.OptOut;
using Xunit;

namespace TCPA.Api.Tests.Unit.OptOut;

/// <summary>
/// Tests for <see cref="OptOutStatusService"/>.
/// Verifies: new number creates record, already-OPT_OUT is idempotent,
/// OPT_IN→OPT_OUT transition, IsOptedOutAsync behavior, fail-closed DB error path.
/// </summary>
public sealed class OptOutStatusServiceTests : IDisposable
{
    private readonly TcpaDbContext _dbContext;
    private readonly Mock<ILogger<OptOutStatusService>> _loggerMock;
    private readonly OptOutStatusService _sut;

    public OptOutStatusServiceTests()
    {
        DbContextOptions<TcpaDbContext> options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TcpaDbContext(options);
        _loggerMock = new Mock<ILogger<OptOutStatusService>>();
        _sut = new OptOutStatusService(_dbContext, _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // -----------------------------------------------------------------------
    // WriteOptOutAsync — new cell number (no prior record)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_CreateOptOutRecord_When_CellNumberIsNew()
    {
        // Arrange
        const string cellNumber = "+12025551234";
        DateTime eventTime = DateTime.UtcNow;

        // Act
        WriteOptOutResult result = await _sut.WriteOptOutAsync(cellNumber, eventTime, "APP-001");

        // Assert
        result.StatusWriteSuccess.Should().BeTrue();
        result.PreviousStatus.Should().Be("OPT_IN");
        result.RecordId.Should().NotBeNull();

        CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
            .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellNumber);
        record.Should().NotBeNull();
        record!.Status.Should().Be(OptOutStatus.OptOut);
    }

    [Fact]
    public async Task Should_ReturnOPT_IN_AsPreviousStatus_When_CellNumberIsNew()
    {
        // Arrange
        const string cellNumber = "+12025551235";
        DateTime eventTime = DateTime.UtcNow;

        // Act
        WriteOptOutResult result = await _sut.WriteOptOutAsync(cellNumber, eventTime, "APP-001");

        // Assert — default-to-OPT_IN assumption per BR-001 / ASM-002
        result.PreviousStatus.Should().Be("OPT_IN");
    }

    // -----------------------------------------------------------------------
    // WriteOptOutAsync — already OPT_OUT (idempotent, no second DB write)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_BeIdempotent_When_CellNumberIsAlreadyOptOut()
    {
        // Arrange — seed an existing OPT_OUT record
        const string cellNumber = "+12025551236";
        Guid existingId = Guid.NewGuid();
        DateTime originalTimestamp = DateTime.UtcNow.AddMinutes(-5);

        _dbContext.OptOutRecords.Add(new CellNumberOptOutRecord
        {
            Id = existingId,
            CellPhoneNumber = cellNumber,
            Status = OptOutStatus.OptOut,
            LastOptOutTimestamp = originalTimestamp,
            CreatedAt = originalTimestamp,
            UpdatedAt = originalTimestamp,
        });
        await _dbContext.SaveChangesAsync();

        int recordCountBefore = await _dbContext.OptOutRecords.CountAsync();

        // Act
        WriteOptOutResult result = await _sut.WriteOptOutAsync(cellNumber, DateTime.UtcNow, "APP-001");

        // Assert — success, same record ID, no new record inserted
        result.StatusWriteSuccess.Should().BeTrue();
        result.PreviousStatus.Should().Be("OPT_OUT");
        result.RecordId.Should().Be(existingId);

        int recordCountAfter = await _dbContext.OptOutRecords.CountAsync();
        recordCountAfter.Should().Be(recordCountBefore, because: "idempotent write must not insert a second record");
    }

    // -----------------------------------------------------------------------
    // WriteOptOutAsync — OPT_IN record updated to OPT_OUT
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_UpdateExistingRecord_When_CellNumberWasOptIn()
    {
        // Arrange — seed an existing OPT_IN record
        const string cellNumber = "+12025551237";
        Guid existingId = Guid.NewGuid();
        DateTime originalTimestamp = DateTime.UtcNow.AddDays(-10);

        _dbContext.OptOutRecords.Add(new CellNumberOptOutRecord
        {
            Id = existingId,
            CellPhoneNumber = cellNumber,
            Status = OptOutStatus.OptIn,
            LastOptOutTimestamp = null,
            CreatedAt = originalTimestamp,
            UpdatedAt = originalTimestamp,
        });
        await _dbContext.SaveChangesAsync();

        // Act
        WriteOptOutResult result = await _sut.WriteOptOutAsync(cellNumber, DateTime.UtcNow, "APP-001");

        // Assert
        result.StatusWriteSuccess.Should().BeTrue();
        result.PreviousStatus.Should().Be("OPT_IN");
        result.RecordId.Should().Be(existingId);

        CellNumberOptOutRecord? updated = await _dbContext.OptOutRecords.FindAsync(existingId);
        updated!.Status.Should().Be(OptOutStatus.OptOut);
    }

    // -----------------------------------------------------------------------
    // WriteOptOutAsync — argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ThrowArgumentException_When_CellPhoneNumberIsNullOrWhiteSpace()
    {
        // Act
        Func<Task> act = async () => await _sut.WriteOptOutAsync(
            "   ", DateTime.UtcNow, "APP-001");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cellPhoneNumber*");
    }

    [Fact]
    public async Task Should_ThrowArgumentException_When_ApplicationIdIsNullOrWhiteSpace()
    {
        // Act
        Func<Task> act = async () => await _sut.WriteOptOutAsync(
            "+12025551238", DateTime.UtcNow, "   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*applicationId*");
    }

    // -----------------------------------------------------------------------
    // WriteOptOutAsync — DB failure returns StatusWriteSuccess=false (fail-open for write)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnFailure_When_DatabaseThrowsOnWrite()
    {
        // Arrange — use a disposed context to simulate DB failure
        var disposedContext = new TcpaDbContext(
            new DbContextOptionsBuilder<TcpaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        disposedContext.Dispose();

        var sut = new OptOutStatusService(disposedContext, _loggerMock.Object);

        // Act
        WriteOptOutResult result = await sut.WriteOptOutAsync(
            "+12025551239", DateTime.UtcNow, "APP-001");

        // Assert — write failure is surfaced, not swallowed
        result.StatusWriteSuccess.Should().BeFalse();
        result.RecordId.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // IsOptedOutAsync — returns true for OPT_OUT record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnTrue_When_CellNumberIsOptOut()
    {
        // Arrange
        const string cellNumber = "+12025551240";
        _dbContext.OptOutRecords.Add(new CellNumberOptOutRecord
        {
            Id = Guid.NewGuid(),
            CellPhoneNumber = cellNumber,
            Status = OptOutStatus.OptOut,
            LastOptOutTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        // Act
        bool isOptedOut = await _sut.IsOptedOutAsync(cellNumber);

        // Assert
        isOptedOut.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // IsOptedOutAsync — returns false for OPT_IN record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnFalse_When_CellNumberIsOptIn()
    {
        // Arrange
        const string cellNumber = "+12025551241";
        _dbContext.OptOutRecords.Add(new CellNumberOptOutRecord
        {
            Id = Guid.NewGuid(),
            CellPhoneNumber = cellNumber,
            Status = OptOutStatus.OptIn,
            LastOptOutTimestamp = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        // Act
        bool isOptedOut = await _sut.IsOptedOutAsync(cellNumber);

        // Assert
        isOptedOut.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // IsOptedOutAsync — returns false (OPT_IN default) for unknown number (BR-001)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnFalse_When_CellNumberHasNoRecord()
    {
        // Arrange — no record in DB for this number
        const string cellNumber = "+12025559999";

        // Act
        bool isOptedOut = await _sut.IsOptedOutAsync(cellNumber);

        // Assert — default to OPT_IN per BR-001 / ASM-002
        isOptedOut.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // IsOptedOutAsync — DB failure re-throws (fail-closed per NFS-005)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_Throw_When_DatabaseIsUnavailableDuringStatusCheck()
    {
        // Arrange — disposed context simulates DB failure
        var disposedContext = new TcpaDbContext(
            new DbContextOptionsBuilder<TcpaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        disposedContext.Dispose();

        var sut = new OptOutStatusService(disposedContext, _loggerMock.Object);

        // Act
        Func<Task> act = async () => await sut.IsOptedOutAsync("+12025551242");

        // Assert — must throw so caller can enforce fail-closed 503 (NFS-005)
        await act.Should().ThrowAsync<Exception>(
            because: "database unavailability must surface to the caller for fail-closed enforcement");
    }

    // -----------------------------------------------------------------------
    // IsOptedOutAsync — argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ThrowArgumentException_When_IsOptedOutCalledWithWhiteSpaceNumber()
    {
        // Act
        Func<Task> act = async () => await _sut.IsOptedOutAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cellPhoneNumber*");
    }
}
