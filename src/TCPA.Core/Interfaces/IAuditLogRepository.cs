using TCPA.Core.Models;

namespace TCPA.Core.Interfaces;

public interface IAuditLogRepository
{
    /// <summary>
    /// Stages an audit log entry on the current DbContext. Does NOT call SaveChangesAsync.
    /// The calling service must commit the transaction. This allows atomic writes with status records.
    /// </summary>
    void Write(AuditLog entry);

    Task<IReadOnlyList<AuditLog>> QueryByPhoneNumberAsync(
        string phoneNumber, DateTime from, DateTime to, CancellationToken ct);
}
