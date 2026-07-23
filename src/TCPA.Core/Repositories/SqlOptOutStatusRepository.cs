using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.Core.Repositories;

/// <summary>
/// SQL Server-backed implementation of IOptOutStatusRepository.
/// Uses separate write and read contexts to support primary/replica routing.
/// </summary>
public class SqlOptOutStatusRepository : IOptOutStatusRepository
{
    private readonly TcpaDbContext _writeCtx;
    private readonly TcpaDbContext _readCtx;

    /// <summary>
    /// Production constructor. Uses keyed DI registration for primary/replica separation.
    /// In tests, pass the same context for both parameters.
    /// </summary>
    public SqlOptOutStatusRepository(
        [FromKeyedServices("primary")] TcpaDbContext writeCtx,
        [FromKeyedServices("replica")] TcpaDbContext readCtx)
    {
        _writeCtx = writeCtx;
        _readCtx = readCtx;
    }

    /// <inheritdoc/>
    public async Task<string> GetStatusAsync(string phoneNumber, CancellationToken ct)
    {
        var record = await _readCtx.OptOutStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber, ct);
        return record?.Status ?? "opted-in";
    }

    /// <inheritdoc/>
    public async Task<bool> IsOptedOutAsync(string phoneNumber, CancellationToken ct)
        => await GetStatusAsync(phoneNumber, ct) == "opted-out";

    /// <inheritdoc/>
    public async Task UpsertOptOutAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt, CancellationToken ct)
    {
        var existing = await _writeCtx.OptOutStatuses
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber, ct);

        if (existing is null)
        {
            _writeCtx.OptOutStatuses.Add(new OptOutStatus
            {
                PhoneNumber = phoneNumber,
                Status = "opted-out",
                EffectiveAt = effectiveAt,
                AuditRecordId = auditRecordId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Status = "opted-out";
            existing.EffectiveAt = effectiveAt;
            existing.AuditRecordId = auditRecordId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _writeCtx.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SetOptedInAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt, CancellationToken ct)
    {
        var existing = await _writeCtx.OptOutStatuses
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber, ct);

        if (existing is null)
        {
            _writeCtx.OptOutStatuses.Add(new OptOutStatus
            {
                PhoneNumber = phoneNumber,
                Status = "opted-in",
                EffectiveAt = effectiveAt,
                AuditRecordId = auditRecordId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Status = "opted-in";
            existing.EffectiveAt = effectiveAt;
            existing.AuditRecordId = auditRecordId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _writeCtx.SaveChangesAsync(ct);
    }
}
