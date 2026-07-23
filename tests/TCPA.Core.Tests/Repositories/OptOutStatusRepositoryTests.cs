using FluentAssertions;
using TCPA.Core.Repositories;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Repositories;

/// <summary>
/// Integration tests for SqlOptOutStatusRepository using a real SQL Server
/// container via Testcontainers. Each test gets a fresh DbContext so there
/// is no state leakage between tests.
/// </summary>
[Collection("SqlServer")]
public class OptOutStatusRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public OptOutStatusRepositoryTests(SqlServerFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task GetStatusAsync_UnknownNumber_ReturnsOptedIn()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlOptOutStatusRepository(ctx, ctx);

        var status = await repo.GetStatusAsync("+10000000001", CancellationToken.None);

        status.Should().Be("opted-in");
    }

    [Fact]
    public async Task UpsertOptOutAsync_NewNumber_WritesOptedOutStatus()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlOptOutStatusRepository(ctx, ctx);
        var phone = "+10000000002";
        var effectiveAt = DateTime.UtcNow;

        await repo.UpsertOptOutAsync(phone, auditRecordId: 1, effectiveAt, CancellationToken.None);

        var status = await repo.GetStatusAsync(phone, CancellationToken.None);
        status.Should().Be("opted-out");
    }

    [Fact]
    public async Task UpsertOptOutAsync_DuplicateCall_DoesNotThrow()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlOptOutStatusRepository(ctx, ctx);
        var phone = "+10000000003";
        var t = DateTime.UtcNow;

        await repo.UpsertOptOutAsync(phone, 1, t, CancellationToken.None);
        var act = () => repo.UpsertOptOutAsync(phone, 2, t.AddSeconds(10), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetOptedInAsync_AfterOptOut_StatusBecomesOptedIn()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlOptOutStatusRepository(ctx, ctx);
        var phone = "+10000000004";

        await repo.UpsertOptOutAsync(phone, 1, DateTime.UtcNow, CancellationToken.None);
        await repo.SetOptedInAsync(phone, 2, DateTime.UtcNow, CancellationToken.None);

        var status = await repo.GetStatusAsync(phone, CancellationToken.None);
        status.Should().Be("opted-in");
    }
}
