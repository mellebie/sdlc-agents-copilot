// Tests for ReportingService
// Source: TASK (Data Services) | SPEC-011, SPEC-012, SPEC-013
// Covers: QueryOptedIn/QueryOptedOut date filtering, 90-day limit, compliance failure detection, success rate

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.Reporting;
using Xunit;

namespace TCPA.Api.Tests.Unit.Reporting;

public sealed class ReportingServiceTests : IDisposable
{
    private readonly TcpaDbContext _dbContext;
    private readonly Mock<ICorrelationIdAccessor> _correlationIdAccessor;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ReportingService _sut;

    // Fixed reference date for all tests
    private static readonly DateTime ReferenceNow = new(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc);

    public ReportingServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TcpaDbContext(options);
        _correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        _correlationIdAccessor.Setup(a => a.CorrelationId).Returns("test-correlation-id");
        _timeProvider = new FakeTimeProvider(ReferenceNow);

        _sut = new ReportingService(
            _dbContext,
            new Mock<ILogger<ReportingService>>().Object,
            _correlationIdAccessor.Object,
            _timeProvider);
    }

    public void Dispose() => _dbContext.Dispose();

    // -----------------------------------------------------------------------
    // QueryOptedInAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryOptedInAsync_Should_ReturnForwardedSmsRecords_WithinDateRange()
    {
        // Arrange
        var inRange = BuildSmsLog("+12025551111", SmsMessageStatus.Forwarded,
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var outOfRange = BuildSmsLog("+12025552222", SmsMessageStatus.Forwarded,
            timestamp: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        await _dbContext.SmsMessageLogs.AddRangeAsync(inRange, outOfRange);
        await _dbContext.SaveChangesAsync();

        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };

        // Act
        var result = await _sut.QueryOptedInAsync(filter);

        // Assert
        result.Records.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.Records.First().CellPhoneNumber.Should().Be("+12025551111");
    }

    [Fact]
    public async Task QueryOptedInAsync_Should_NotReturnBlockedMessages()
    {
        // Arrange — suppressed messages must NOT appear in the opted-in report
        var forwarded = BuildSmsLog("+12025551111", SmsMessageStatus.Forwarded);
        var suppressed = BuildSmsLog("+12025552222", SmsMessageStatus.Suppressed);

        await _dbContext.SmsMessageLogs.AddRangeAsync(forwarded, suppressed);
        await _dbContext.SaveChangesAsync();

        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };

        // Act
        var result = await _sut.QueryOptedInAsync(filter);

        // Assert — only forwarded messages
        result.Records.Should().HaveCount(1);
        result.Records.First().CellPhoneNumber.Should().Be("+12025551111");
    }

    [Fact]
    public async Task QueryOptedInAsync_Should_ThrowArgumentException_When_DateRangeExceeds90Days()
    {
        // Arrange
        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) // > 90 days
        };

        // Act
        var act = async () => await _sut.QueryOptedInAsync(filter);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*90 days*");
    }

    [Fact]
    public async Task QueryOptedInAsync_Should_ThrowArgumentException_When_FromIsAfterTo()
    {
        // Arrange
        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var act = async () => await _sut.QueryOptedInAsync(filter);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must not be later*");
    }

    // -----------------------------------------------------------------------
    // QueryOptedOutAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryOptedOutAsync_Should_ReturnBlockedSmsAuditEntries_WithinDateRange()
    {
        // Arrange
        var inRange = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025551111",
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var outOfRange = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025552222",
            timestamp: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        await _dbContext.AuditLogEntries.AddRangeAsync(inRange, outOfRange);
        await _dbContext.SaveChangesAsync();

        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };

        // Act
        var result = await _sut.QueryOptedOutAsync(filter);

        // Assert
        result.Records.Should().HaveCount(1);
        result.Records.First().CellPhoneNumber.Should().Be("+12025551111");
    }

    [Fact]
    public async Task QueryOptedOutAsync_Should_NotReturnOptOutEvents_OnlyBlockedOutbound()
    {
        // Arrange — opt-out events are a different event type from blocked outbound
        var blockedOutbound = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025551111");
        var optOutEvent = BuildAuditEntry(AuditEventType.OptOut, "+12025552222");

        await _dbContext.AuditLogEntries.AddRangeAsync(blockedOutbound, optOutEvent);
        await _dbContext.SaveChangesAsync();

        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };

        // Act
        var result = await _sut.QueryOptedOutAsync(filter);

        // Assert — only BlockedOutbound entries
        result.Records.Should().HaveCount(1);
        result.Records.First().CellPhoneNumber.Should().Be("+12025551111");
    }

    [Fact]
    public async Task QueryOptedOutAsync_Should_ThrowArgumentException_When_DateRangeExceeds90Days()
    {
        // Arrange
        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act & Assert
        await _sut.Invoking(s => s.QueryOptedOutAsync(filter))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*90 days*");
    }

    [Fact]
    public async Task QueryOptedOutAsync_Should_ThrowArgumentException_When_FromIsAfterTo()
    {
        // Arrange
        var filter = new ReportQueryFilter
        {
            From = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act & Assert
        await _sut.Invoking(s => s.QueryOptedOutAsync(filter))
            .Should().ThrowAsync<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // GenerateWeeklyReportAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateWeeklyReportAsync_Should_Return100PercentSuccessRate_When_NoComplianceFailures()
    {
        // Arrange — only blocked messages, no forwarded messages to opted-out numbers
        var blockedEntry = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025551111");
        await _dbContext.AuditLogEntries.AddAsync(blockedEntry);
        await _dbContext.SaveChangesAsync();

        var periodStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 7, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var report = await _sut.GenerateWeeklyReportAsync(periodStart, periodEnd);

        // Assert — 100% when there are blocks but no missed blocks
        report.OptOutEnforcementSuccessRate.Should().Be(100.0);
        report.ComplianceFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_Should_Return100Percent_When_NoActivity()
    {
        // Arrange — empty database
        var periodStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 7, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var report = await _sut.GenerateWeeklyReportAsync(periodStart, periodEnd);

        // Assert — no attempts means 100% (no failures)
        report.OptOutEnforcementSuccessRate.Should().Be(100.0);
        report.TotalForwardedCount.Should().Be(0);
        report.TotalBlockedCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_Should_DetectComplianceFailure_When_ForwardedMessageExistsForOptedOutNumber()
    {
        // Arrange — a forwarded message exists for a number that was also blocked in the same period
        // This represents a compliance failure: the number was opted out but a message got through
        var cellNumber = "+12025551234";
        var periodStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 7, 23, 59, 59, DateTimeKind.Utc);

        // A blocked outbound event (OPT_OUT enforced correctly)
        var blockedEntry = BuildAuditEntry(AuditEventType.BlockedOutbound, cellNumber,
            timestamp: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc));

        // A forwarded message to the same opted-out number (compliance failure)
        var forwardedMessage = BuildSmsLog(cellNumber, SmsMessageStatus.Forwarded,
            timestamp: new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));

        await _dbContext.AuditLogEntries.AddAsync(blockedEntry);
        await _dbContext.SmsMessageLogs.AddAsync(forwardedMessage);
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _sut.GenerateWeeklyReportAsync(periodStart, periodEnd);

        // Assert — compliance failure detected
        report.ComplianceFailures.Should().HaveCount(1);
        report.ComplianceFailures.First().MaskedCellPhoneNumber.Should().Contain("1234"); // last 4 digits
        report.OptOutEnforcementSuccessRate.Should().BeLessThan(100.0);
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_Should_ThrowArgumentException_When_PeriodStartIsAfterPeriodEnd()
    {
        // Arrange
        var periodStart = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc); // end before start

        // Act
        var act = async () => await _sut.GenerateWeeklyReportAsync(periodStart, periodEnd);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("periodStart");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SmsMessageLog BuildSmsLog(
        string cellNumber,
        SmsMessageStatus status,
        DateTime? timestamp = null)
    {
        return new SmsMessageLog
        {
            Id = Guid.NewGuid(),
            CellPhoneNumber = cellNumber,
            ApplicationName = "GCMA",
            Direction = SmsDirection.Outbound,
            MessageContent = "Test message",
            Status = status,
            Timestamp = timestamp ?? new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private static AuditLogEntry BuildAuditEntry(
        AuditEventType eventType,
        string cellPhoneNumber,
        DateTime? timestamp = null)
    {
        return new AuditLogEntry
        {
            RecordId = Guid.NewGuid(),
            EventType = eventType,
            EventTimestamp = timestamp ?? new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            CellPhoneNumber = cellPhoneNumber,
            OriginatingCoolTextAccountId = "ACC-001",
            OriginatingApplicationName = "GCMA",
            SystemResponse = eventType == AuditEventType.BlockedOutbound
                ? "MESSAGE_SUPPRESSED_OPT_OUT"
                : "OPT_OUT_STATUS_WRITTEN",
            SuppressionReason = eventType == AuditEventType.BlockedOutbound ? "OPT_OUT" : null,
            CreatedAt = DateTime.UtcNow
        };
    }
}
