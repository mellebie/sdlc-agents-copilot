// src/TCPA.Api/Services/OptOut/IOptOutStatusService.cs
// TCPA Compliance Engine — Opt-Out Status Write Service Interface
// Source: TASK-018 | SPEC-004 | STORY-005
// Business Rules: BR-016 through BR-020

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Result returned by <see cref="IOptOutStatusService.WriteOptOutAsync"/>.
/// </summary>
public sealed record WriteOptOutResult
{
    /// <summary>
    /// <c>true</c> when the opt-out record was successfully persisted or
    /// when the number was already OPT-OUT (idempotent case).
    /// </summary>
    public bool StatusWriteSuccess { get; init; }

    /// <summary>
    /// The status held by the cell number immediately before this write.
    /// "OPT_IN" for a new or previously opted-in number; "OPT_OUT" for
    /// a number that was already opted out.
    /// </summary>
    public string PreviousStatus { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier of the underlying <c>CellNumberOptOutRecord</c> row, or
    /// <c>null</c> when <see cref="StatusWriteSuccess"/> is <c>false</c>.
    /// </summary>
    public Guid? RecordId { get; init; }
}

/// <summary>
/// Manages the authoritative OPT-OUT status for a cell phone number.
/// An opt-out is global across all in-scope SCG applications (BR-016).
/// </summary>
public interface IOptOutStatusService
{
    /// <summary>
    /// Atomically writes an OPT-OUT status for <paramref name="cellPhoneNumber"/>.
    /// If the number is already OPT-OUT the operation is a no-op and returns
    /// success with <c>PreviousStatus = "OPT_OUT"</c> (BR-019 — idempotent).
    /// </summary>
    /// <param name="cellPhoneNumber">
    /// E.164 cell phone number of the customer (PII — never log raw value).
    /// </param>
    /// <param name="eventTimestamp">
    /// Timestamp of the inbound message receipt, NOT the time of the DB write
    /// (BR-018).
    /// </param>
    /// <param name="applicationId">
    /// Identifier of the SCG application whose Cool Text account received the
    /// opt-out keyword; used for audit context.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation requests.</param>
    /// <returns>A <see cref="WriteOptOutResult"/> describing the outcome.</returns>
    Task<WriteOptOutResult> WriteOptOutAsync(
        string cellPhoneNumber,
        DateTime eventTimestamp,
        string applicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether <paramref name="cellPhoneNumber"/> is currently OPT-OUT.
    /// Returns <c>false</c> (treated as OPT-IN) when no record exists (BR-001 /
    /// ASM-002 — default to OPT-IN).
    /// Throws when the database is unavailable — callers must handle this as a
    /// fail-closed 503 response (NFS-005).
    /// </summary>
    /// <param name="cellPhoneNumber">E.164 cell phone number (PII).</param>
    /// <param name="cancellationToken">Propagates cancellation requests.</param>
    /// <returns>
    /// <c>true</c> if the number has an active OPT-OUT status; otherwise
    /// <c>false</c>.
    /// </returns>
    Task<bool> IsOptedOutAsync(
        string cellPhoneNumber,
        CancellationToken cancellationToken = default);
}
