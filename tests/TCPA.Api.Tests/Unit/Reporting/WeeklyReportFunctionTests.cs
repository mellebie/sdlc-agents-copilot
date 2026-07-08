// Tests for WeeklyComplianceReportFunction.CalculatePriorWeekPeriod
// Source: TASK (Data Services) | SPEC-013 | TASK-047
// Covers: period calculation correctness, Monday edge case

using FluentAssertions;
using TCPA.Scheduler;
using Xunit;

namespace TCPA.Api.Tests.Unit.Reporting;

public sealed class WeeklyReportFunctionTests
{
    // -----------------------------------------------------------------------
    // CalculatePriorWeekPeriod — period calculation
    // The function fires every Monday 06:00 UTC and must report the PRIOR week.
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnPriorMondayMidnightUTC_AsPeriodStart()
    {
        // Arrange — triggered on Monday 2026-06-29 06:00 UTC
        var triggerTime = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc); // Monday

        // Act
        var (periodStart, _) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(triggerTime);

        // Assert — period start must be Monday 2026-06-22 00:00:00 UTC (the week before)
        periodStart.Should().Be(new DateTime(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc));
        periodStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
        periodStart.TimeOfDay.Should().Be(TimeSpan.Zero);
        periodStart.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnPriorSunday235959UTC_AsPeriodEnd()
    {
        // Arrange — triggered on Monday 2026-06-29 06:00 UTC
        var triggerTime = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc); // Monday

        // Act
        var (_, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(triggerTime);

        // Assert — period end must be Sunday 2026-06-28 23:59:59 UTC
        periodEnd.Should().Be(new DateTime(2026, 6, 28, 23, 59, 59, DateTimeKind.Utc));
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        periodEnd.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnPreviousWeek_When_CalledOnMonday()
    {
        // Arrange — this is the key edge case: called ON a Monday must return the PRIOR week
        // Monday 2026-06-29 → prior week is Mon 2026-06-22 to Sun 2026-06-28
        var mondayTrigger = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc);

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(mondayTrigger);

        // Assert — must NOT include the current week's Monday (2026-06-29)
        periodStart.Date.Should().Be(new DateTime(2026, 6, 22));
        periodEnd.Date.Should().Be(new DateTime(2026, 6, 28));

        // The trigger date itself must NOT be inside the reporting period
        mondayTrigger.Date.Should().BeAfter(periodEnd.Date);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnExactly7DayPeriod()
    {
        // Arrange
        var triggerTime = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc);

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(triggerTime);

        // Assert — end of Sunday 23:59:59 minus start of Monday 00:00:00 = exactly 6 days 23:59:59
        var duration = periodEnd - periodStart;
        duration.TotalSeconds.Should().BeApproximately(TimeSpan.FromDays(7).TotalSeconds - 1, precision: 1.0);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnCorrectPeriod_When_CalledOnWednesday()
    {
        // Arrange — mid-week trigger (e.g., manual re-run on a Wednesday)
        var wednesdayTrigger = new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc); // Wednesday

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(wednesdayTrigger);

        // Assert — prior Mon–Sun: 2026-06-22 to 2026-06-28
        periodStart.Date.Should().Be(new DateTime(2026, 6, 22));
        periodEnd.Date.Should().Be(new DateTime(2026, 6, 28));
        periodStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_ReturnCorrectPeriod_When_CalledOnSunday()
    {
        // Arrange — Sunday trigger (not a normal schedule, but must handle it correctly)
        var sundayTrigger = new DateTime(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc); // Sunday

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(sundayTrigger);

        // Assert — if triggered on Sunday 2026-06-28, prior week = Mon 2026-06-15 to Sun 2026-06-21
        periodStart.Date.Should().Be(new DateTime(2026, 6, 15));
        periodEnd.Date.Should().Be(new DateTime(2026, 6, 21));
        periodStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_HandleYearBoundaryCorrectly()
    {
        // Arrange — trigger on Monday 2026-01-05 (first Monday of the year; prior week spans year boundary)
        var triggerTime = new DateTime(2026, 1, 5, 6, 0, 0, DateTimeKind.Utc);

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(triggerTime);

        // Assert — prior week: Mon 2025-12-29 to Sun 2026-01-04
        periodStart.Date.Should().Be(new DateTime(2025, 12, 29));
        periodEnd.Date.Should().Be(new DateTime(2026, 1, 4));
        periodStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void CalculatePriorWeekPeriod_Should_AlwaysReturnUtcKind()
    {
        // Arrange
        var triggerTime = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc);

        // Act
        var (periodStart, periodEnd) = WeeklyComplianceReportFunction.CalculatePriorWeekPeriod(triggerTime);

        // Assert — timestamps must be UTC to avoid timezone issues in compliance reporting
        periodStart.Kind.Should().Be(DateTimeKind.Utc);
        periodEnd.Kind.Should().Be(DateTimeKind.Utc);
    }
}
