using Microsoft.EntityFrameworkCore;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.Core.Repositories;

/// <summary>
/// SQL Server implementation of the idempotency repository for processed messages.
/// Stores records keyed by (messageId, endpoint) to prevent duplicate processing.
/// </summary>
public class SqlProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly TcpaDbContext _ctx;

    /// <summary>Initializes a new instance with the primary DbContext.</summary>
    public SqlProcessedMessageRepository(TcpaDbContext ctx) => _ctx = ctx;

    /// <summary>
    /// Queries for an existing processed message by messageId and endpoint.
    /// </summary>
    public async Task<ProcessedMessage?> FindAsync(string messageId, string endpoint, CancellationToken ct)
        => await _ctx.ProcessedMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MessageId == messageId && m.Endpoint == endpoint, ct);

    /// <summary>
    /// Persists a new processed message record. The record is immediately committed.
    /// </summary>
    public async Task AddAsync(ProcessedMessage entry, CancellationToken ct)
    {
        _ctx.ProcessedMessages.Add(entry);
        await _ctx.SaveChangesAsync(ct);
    }
}
