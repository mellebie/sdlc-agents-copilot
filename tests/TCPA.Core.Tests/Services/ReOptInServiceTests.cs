using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TCPA.Core.Models;
using TCPA.Core.Repositories;
using TCPA.Core.Services;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Services;

/// <summary>
/// Integration tests for ReOptInService. Uses a real SQL Server container via SqlServerFixture.
/// The [Collection("SqlServer")] attribute shares the fixture across all test classes in the collection;
/// the fixture is injected by xUnit via the constructor, not IClassFixture.
/// </summary>
[Collection("SqlServer")]
public class ReOptInServiceTests
{
    private readonly SqlServerFixture _fixture;

    public ReOptInServiceTests(SqlServerFixture f) => _fixture = f;

    [Fact]
    public async Task ExecuteAsync_AfterOptOut_StatusBecomesOptedIn()
    {
        await using var ctx = _fixture.CreateContext();
        var statusRepo = new SqlOptOutStatusRepository(ctx, ctx);
        var auditRepo = new SqlAuditLogRepository(ctx, ctx);
        var sut = new ReOptInService(ctx, statusRepo, auditRepo);
        var phone = "+10000000020";

        // First opt out
        var auditEntry = new AuditLog { EventType = AuditEventType.OptOutWritten, PhoneNumber = phone, OccurredAt = DateTime.UtcNow };
        auditRepo.Write(auditEntry);
        await ctx.SaveChangesAsync();
        await statusRepo.UpsertOptOutAsync(phone, auditEntry.Id, DateTime.UtcNow, CancellationToken.None);

        // Now re-opt-in
        var result = await sut.ExecuteAsync(phone, "agent-001", "Customer called to reverse STOP", CancellationToken.None);

        result.ReOptInId.Should().BeGreaterThan(0);
        var status = await statusRepo.GetStatusAsync(phone, CancellationToken.None);
        status.Should().Be("opted-in");
    }

    [Fact]
    public async Task ExecuteAsync_ForNumberWithNoPriorOptOut_SucceedsWithAnomalyFlag()
    {
        await using var ctx = _fixture.CreateContext();
        var statusRepo = new SqlOptOutStatusRepository(ctx, ctx);
        var auditRepo = new SqlAuditLogRepository(ctx, ctx);
        var sut = new ReOptInService(ctx, statusRepo, auditRepo);
        var phone = "+10000000021";

        var result = await sut.ExecuteAsync(phone, "agent-002", "Customer never opted out", CancellationToken.None);

        result.ReOptInId.Should().BeGreaterThan(0);
        var auditRecord = await ctx.AuditLogs.FindAsync(result.ReOptInId);
        auditRecord!.AnomalyFlag.Should().BeTrue();
    }
}
