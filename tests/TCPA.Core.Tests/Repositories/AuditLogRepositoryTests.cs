using FluentAssertions;
using TCPA.Core.Models;
using TCPA.Core.Repositories;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Repositories;

[Collection("SqlServer")]
public class AuditLogRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public AuditLogRepositoryTests(SqlServerFixture f) => _fixture = f;

    [Fact]
    public async Task WriteAsync_ValidEntry_PersistsWithId()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlAuditLogRepository(ctx, ctx);

        var entry = new AuditLog
        {
            EventType = AuditEventType.OptOutWritten,
            PhoneNumber = "+10000000010",
            OccurredAt = DateTime.UtcNow,
            ApplicationId = "BizTalk",
            MessageId = "msg-001"
        };

        // WriteAsync stages but does not commit — caller must SaveChanges
        repo.Write(entry);
        await ctx.SaveChangesAsync();

        entry.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QueryByPhoneNumberAsync_ReturnsRecordsInDateRange()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlAuditLogRepository(ctx, ctx);
        var phone = "+10000000011";
        var now = DateTime.UtcNow;

        var entry = new AuditLog { EventType = AuditEventType.OptOutWritten, PhoneNumber = phone, OccurredAt = now };
        repo.Write(entry);
        await ctx.SaveChangesAsync();

        var results = await repo.QueryByPhoneNumberAsync(phone, now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);

        results.Should().ContainSingle(x => x.PhoneNumber == phone);
    }

    [Fact]
    public async Task QueryByPhoneNumberAsync_ExcludesRecordsOutsideDateRange()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlAuditLogRepository(ctx, ctx);
        var phone = "+10000000012";
        var yesterday = DateTime.UtcNow.AddDays(-1);

        var entry = new AuditLog { EventType = AuditEventType.ConfirmationDispatched, PhoneNumber = phone, OccurredAt = yesterday };
        repo.Write(entry);
        await ctx.SaveChangesAsync();

        // Query for today only — yesterday's record must not appear
        var results = await repo.QueryByPhoneNumberAsync(phone, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, CancellationToken.None);

        results.Should().BeEmpty();
    }
}
