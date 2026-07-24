using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Messaging;

namespace TCPA.MessageProcessor.Services;

/// <summary>
/// Atomically writes an opt-out status record and a paired audit log entry within a single
/// database transaction. Handles the duplicate case by writing an OptOutDuplicate audit entry
/// instead of re-upserting the opt-out status.
/// </summary>
public class OptOutProcessingService : IOptOutProcessingService
{
    private readonly TcpaDbContext _writeCtx;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<OptOutProcessingService> _logger;

    public OptOutProcessingService(
        [FromKeyedServices("primary")] TcpaDbContext writeCtx,
        IAuditLogRepository auditRepo,
        IOptOutStatusRepository statusRepo,
        IPhoneNumberHasher hasher,
        ILogger<OptOutProcessingService> logger)
    {
        _writeCtx = writeCtx;
        _auditRepo = auditRepo;
        _statusRepo = statusRepo;
        _hasher = hasher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OptOutResult> ProcessOptOutAsync(InboundMessageEvent @event, CancellationToken ct)
    {
        var isAlreadyOptedOut = await _statusRepo.IsOptedOutAsync(@event.From, ct);
        var eventType = isAlreadyOptedOut ? AuditEventType.OptOutDuplicate : AuditEventType.OptOutWritten;

        var auditEntry = new AuditLog
        {
            EventType = eventType,
            PhoneNumber = @event.From,
            OccurredAt = DateTime.UtcNow,
            ApplicationId = @event.ApplicationId,
            MessageId = @event.MessageId,
            Details = JsonSerializer.Serialize(new
            {
                keyword = @event.Body.Trim(),
                provider = @event.Provider
            })
        };

        // Transaction guard: IsRelational() is false for InMemory (unit tests), true for SQL Server (production).
        // This allows unit tests to run without requiring a real database while still validating logic.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_writeCtx.Database.IsRelational())
            tx = await _writeCtx.Database.BeginTransactionAsync(ct);

        try
        {
            // Stage the audit entry on the DbContext; SaveChangesAsync assigns its generated Id.
            _auditRepo.Write(auditEntry);
            await _writeCtx.SaveChangesAsync(ct);

            // Only upsert the opt-out status when the number is not already opted out.
            // Duplicate opt-outs are recorded for auditing but do not update the status record.
            if (!isAlreadyOptedOut)
            {
                await _statusRepo.UpsertOptOutAsync(@event.From, auditEntry.Id, DateTime.UtcNow, ct);
            }

            if (tx is not null)
                await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Opt-out processed. EventType={EventType} PhoneHash={PhoneHash} MessageId={MessageId}",
                eventType,
                _hasher.Hash(@event.From),
                @event.MessageId);

            return new OptOutResult(!isAlreadyOptedOut, auditEntry.Id);
        }
        catch
        {
            if (tx is not null)
            {
                try { await tx.RollbackAsync(ct); } catch { /* best effort — do not mask original exception */ }
            }
            throw;
        }
        finally
        {
            if (tx is not null)
                await tx.DisposeAsync();
        }
    }
}
