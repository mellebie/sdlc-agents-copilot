namespace TCPA.Api.Domain;

/// <summary>
/// Represents the delivery outcome of the opt-out confirmation SMS sent to a customer
/// after their opt-out keyword is processed (SPEC-005).
/// </summary>
public enum ConfirmationSmsStatus
{
    /// <summary>
    /// The confirmation SMS was successfully dispatched to the Cool Text platform within
    /// the 60-second SLA window (NFS-001).
    /// </summary>
    Sent = 1,

    /// <summary>
    /// The confirmation SMS could not be delivered after one retry attempt.
    /// The opt-out status remains OPT_OUT; only the confirmation delivery failed.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// No confirmation SMS was sent because the cell number was already in OPT_OUT status
    /// before this opt-out event was processed (idempotent re-opt-out scenario).
    /// </summary>
    NotSent = 3
}
