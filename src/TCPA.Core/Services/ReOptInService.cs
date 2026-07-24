using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.Core.Services;

public record ReOptInResult(long ReOptInId, DateTime EffectiveAt);

public interface IReOptInService
{
    Task<ReOptInResult> ExecuteAsync(string phoneNumber, string agentId, string reason, CancellationToken ct);
}

/// <summary>
/// Performs an atomic re-opt-in: writes an audit log entry and updates the opt-out status
/// in a single database transaction. If either write fails, both roll back.
/// </summary>
public class ReOptInService : IReOptInService
{
    private readonly TcpaDbContext _writeCtx;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IAuditLogRepository _auditRepo;

    public ReOptInService(
        [FromKeyedServices("primary")] TcpaDbContext writeContext,
        IOptOutStatusRepository statusRepo,
        IAuditLogRepository auditRepo)
    {
        _writeCtx = writeContext;
        _statusRepo = statusRepo;
        _auditRepo = auditRepo;
    }

    /// <summary>
    /// Executes the re-opt-in workflow atomically.
    /// Sets an anomaly flag on the audit entry if the number was not previously opted out.
    /// </summary>
    public async Task<ReOptInResult> ExecuteAsync(string phoneNumber, string agentId, string reason, CancellationToken ct)
    {
        var effectiveAt = DateTime.UtcNow;
        var currentStatus = await _statusRepo.GetStatusAsync(phoneNumber, ct);
        var hasNoPriorOptOut = currentStatus == "opted-in";

        // Transaction guard: IsRelational() is false for InMemory (unit tests), true for SQL Server
        // (production). This mirrors the pattern in OptOutProcessingService and allows the re-opt-in
        // path to be unit-tested with an InMemory DbContext without throwing InvalidOperationException.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_writeCtx.Database.IsRelational())
            transaction = await _writeCtx.Database.BeginTransactionAsync(ct);
        await using var tx = transaction;
        try
        {
            var auditEntry = new AuditLog
            {
                EventType = AuditEventType.ReOptIn,
                PhoneNumber = phoneNumber,
                OccurredAt = effectiveAt,
                AgentId = agentId,
                Details = System.Text.Json.JsonSerializer.Serialize(new { reason }),
                AnomalyFlag = hasNoPriorOptOut
            };
            _auditRepo.Write(auditEntry);
            // Persist audit entry within the transaction to obtain its generated Id
            await _writeCtx.SaveChangesAsync(ct);

            await _statusRepo.SetOptedInAsync(phoneNumber, auditEntry.Id, effectiveAt, ct);

            if (tx is not null)
                await tx.CommitAsync(ct);
            return new ReOptInResult(auditEntry.Id, effectiveAt);
        }
        catch
        {
            if (tx is not null)
            {
                try { await tx.RollbackAsync(ct); } catch { /* best effort */ }
            }
            throw;
        }
    }
}
