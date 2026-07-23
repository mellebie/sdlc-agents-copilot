using FluentAssertions;
using TCPA.Core.Exceptions;
using TCPA.Core.Models;
using TCPA.Core.Repositories;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Repositories;

[Collection("SqlServer")]
public class SystemConfigRepositoryTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;
    public SystemConfigRepositoryTests(SqlServerFixture f) => _fixture = f;

    [Fact]
    public async Task GetValueAsync_ExistingKey_ReturnsValue()
    {
        await using var ctx = _fixture.CreateContext();
        ctx.SystemConfigs.Add(new SystemConfig { Key = "TestKey1", Value = "TestValue1" });
        await ctx.SaveChangesAsync();
        var repo = new SqlSystemConfigRepository(ctx);

        var value = await repo.GetValueAsync("TestKey1", CancellationToken.None);

        value.Should().Be("TestValue1");
    }

    [Fact]
    public async Task GetValueAsync_MissingKey_ReturnsNull()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlSystemConfigRepository(ctx);

        var value = await repo.GetValueAsync("NonExistentKey", CancellationToken.None);

        value.Should().BeNull();
    }

    [Fact]
    public async Task GetRequiredValueAsync_MissingKey_ThrowsConfigurationException()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new SqlSystemConfigRepository(ctx);

        var act = () => repo.GetRequiredValueAsync("MissingRequired", CancellationToken.None);

        await act.Should().ThrowAsync<ConfigurationException>()
            .WithMessage("*MissingRequired*");
    }

    [Fact]
    public async Task GetValueAsync_AfterUpdate_ReturnsNewValue()
    {
        await using var ctx = _fixture.CreateContext();
        var config = new SystemConfig { Key = "MutableKey", Value = "Original" };
        ctx.SystemConfigs.Add(config);
        await ctx.SaveChangesAsync();

        // Update value directly
        config.Value = "Updated";
        await ctx.SaveChangesAsync();

        var repo = new SqlSystemConfigRepository(ctx);
        var value = await repo.GetValueAsync("MutableKey", CancellationToken.None);
        value.Should().Be("Updated");
    }
}
