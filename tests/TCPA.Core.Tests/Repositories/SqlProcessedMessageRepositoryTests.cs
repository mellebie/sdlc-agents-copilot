using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TCPA.Core.Models;
using TCPA.Core.Repositories;
using TCPA.Core.Tests.Infrastructure;

namespace TCPA.Core.Tests.Repositories;

[Collection("SqlServer")]
public class SqlProcessedMessageRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public SqlProcessedMessageRepositoryTests(SqlServerFixture f) => _fixture = f;

    [Fact]
    public async Task FindAsync_WhenMessageNotProcessed_ReturnsNull()
    {
        await using var ctx = _fixture.CreateContext();
        var sut = new SqlProcessedMessageRepository(ctx);

        var result = await sut.FindAsync("nonexistent-id", "webhook", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_WhenMessageProcessed_ReturnsRecord()
    {
        await using var ctx = _fixture.CreateContext();
        var sut = new SqlProcessedMessageRepository(ctx);
        var messageId = $"test-{Guid.NewGuid()}";
        var entry = new ProcessedMessage
        {
            MessageId = messageId,
            InternalId = Guid.NewGuid(),
            ResponseStatus = "received",
            ProcessedAt = DateTime.UtcNow,
            Endpoint = "webhook"
        };
        await sut.AddAsync(entry, CancellationToken.None);

        var result = await sut.FindAsync(messageId, "webhook", CancellationToken.None);

        result.Should().NotBeNull();
        result!.MessageId.Should().Be(messageId);
        result.ResponseStatus.Should().Be("received");
    }

    [Fact]
    public async Task FindAsync_SameMessageId_DifferentEndpoint_ReturnsNull()
    {
        await using var ctx = _fixture.CreateContext();
        var sut = new SqlProcessedMessageRepository(ctx);
        var messageId = $"test-{Guid.NewGuid()}";
        await sut.AddAsync(new ProcessedMessage
        {
            MessageId = messageId,
            InternalId = Guid.NewGuid(),
            ResponseStatus = "received",
            ProcessedAt = DateTime.UtcNow,
            Endpoint = "webhook"
        }, CancellationToken.None);

        var result = await sut.FindAsync(messageId, "outbound", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_SecondAdd_SameMessageId_ThrowsDbUpdateException()
    {
        await using var ctx = _fixture.CreateContext();
        var sut = new SqlProcessedMessageRepository(ctx);
        var messageId = $"test-{Guid.NewGuid()}";
        var first = new ProcessedMessage
        {
            MessageId = messageId,
            InternalId = Guid.NewGuid(),
            ResponseStatus = "queued",
            ProcessedAt = DateTime.UtcNow,
            Endpoint = "outbound"
        };
        await sut.AddAsync(first, CancellationToken.None);

        // Create a DIFFERENT object with the same PK — avoids EF tracking collision
        // and ensures the DB constraint violation is what we test, not an EF error.
        var duplicate = new ProcessedMessage
        {
            MessageId = messageId,        // same PK — triggers DB constraint
            InternalId = Guid.NewGuid(),  // different — avoids EF tracking the same instance
            ResponseStatus = "received",
            ProcessedAt = DateTime.UtcNow,
            Endpoint = "outbound"
        };
        var act = async () => await sut.AddAsync(duplicate, CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
