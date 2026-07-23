using FluentAssertions;
using TCPA.Core.Models;
using TCPA.Core.Repositories;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Repositories;

[Collection("SqlServer")]
public class CoolTextAccountRepositoryTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;
    public CoolTextAccountRepositoryTests(SqlServerFixture f) => _fixture = f;

    [Fact]
    public async Task GetByAccountNumberAsync_RegisteredAccount_ReturnsEntity()
    {
        await using var ctx = _fixture.CreateContext();
        // Seed test data
        ctx.CoolTextAccounts.Add(new CoolTextAccount
        {
            AccountNumber = "CT-TEST-001",
            ApplicationId = "BizTalk",
            ApplicationName = "BizTalk Integration",
            CallbackUrl = "https://biztalk.local/callback",
            IsActive = true
        });
        await ctx.SaveChangesAsync();

        var repo = new SqlCoolTextAccountRepository(ctx);
        var account = await repo.GetByAccountNumberAsync("CT-TEST-001", CancellationToken.None);

        account.Should().NotBeNull();
        account!.ApplicationId.Should().Be("BizTalk");
    }

    [Fact]
    public async Task GetByAccountNumberAsync_UnregisteredAccount_ReturnsNull()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlCoolTextAccountRepository(ctx);

        var account = await repo.GetByAccountNumberAsync("CT-DOES-NOT-EXIST", CancellationToken.None);

        account.Should().BeNull();
    }
}
