namespace TCPA.Core.Interfaces;

public interface IOptOutStatusRepository
{
    /// <summary>Returns "opted-out" or "opted-in". Never returns null. Unknown numbers return "opted-in".</summary>
    Task<string> GetStatusAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Upsert: creates opted-out record or updates existing. Idempotent.</summary>
    Task UpsertOptOutAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt, CancellationToken ct);

    /// <summary>Set status to opted-in (re-opt-in path).</summary>
    Task SetOptedInAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt, CancellationToken ct);

    /// <summary>Returns true if the phone number currently has status "opted-out".</summary>
    Task<bool> IsOptedOutAsync(string phoneNumber, CancellationToken ct);
}
