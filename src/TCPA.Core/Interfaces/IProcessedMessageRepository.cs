using TCPA.Core.Models;

namespace TCPA.Core.Interfaces;

/// <summary>
/// Repository for storing and retrieving processed message records.
/// Used to enforce idempotency on inbound webhook and outbound submission requests.
/// </summary>
public interface IProcessedMessageRepository
{
    /// <summary>
    /// Returns the existing record if the messageId + endpoint combination was already processed; null otherwise.
    /// </summary>
    /// <param name="messageId">The provider messageId or caller correlationId.</param>
    /// <param name="endpoint">The endpoint that handled the message ("webhook" or "outbound").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ProcessedMessage if found; null if not yet processed.</returns>
    Task<ProcessedMessage?> FindAsync(string messageId, string endpoint, CancellationToken ct);

    /// <summary>
    /// Persists a new processed-message record to the database.
    /// Callers must call FindAsync first to verify the record does not already exist,
    /// otherwise this will throw a DbUpdateException.
    /// </summary>
    /// <param name="entry">The ProcessedMessage to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(ProcessedMessage entry, CancellationToken ct);
}
