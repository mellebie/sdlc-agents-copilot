using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.Core.Repositories;

public class SqlAuditLogRepository : IAuditLogRepository
{
    private readonly TcpaDbContext _writeCtx;
    private readonly TcpaDbContext _readCtx;

    public SqlAuditLogRepository(
        [FromKeyedServices("primary")] TcpaDbContext writeContext,
        [FromKeyedServices("replica")] TcpaDbContext readContext)
    {
        _writeCtx = writeContext;
        _readCtx = readContext;
    }

    public void Write(AuditLog entry)
    {
        // Stage only — caller calls SaveChangesAsync to commit
        _writeCtx.AuditLogs.Add(entry);
    }

    public async Task<IReadOnlyList<AuditLog>> QueryByPhoneNumberAsync(
        string phoneNumber, DateTime from, DateTime to, CancellationToken ct)
    {
        return await _readCtx.AuditLogs
            .AsNoTracking()
            .Where(x => x.PhoneNumber == phoneNumber && x.OccurredAt >= from && x.OccurredAt <= to)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(ct);
    }
}
