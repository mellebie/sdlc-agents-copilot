namespace TCPA.Api.Domain;

/// <summary>
/// Enumerates the categories of compliance-relevant events recorded in the immutable audit log.
/// Every audit log entry has exactly one event type.
/// </summary>
public enum AuditEventType
{
    /// <summary>
    /// A customer sent an opt-out keyword (STOP, QUIT, etc.) and their status was written
    /// or confirmed as OPT_OUT in the compliance database.
    /// </summary>
    OptOut = 1,

    /// <summary>
    /// An outbound SMS was suppressed because the destination cell number's status was OPT_OUT
    /// at the time of the send attempt. Each suppressed attempt is an independent record.
    /// </summary>
    BlockedOutbound = 2,

    /// <summary>
    /// A Help Desk agent or Compliance Officer manually updated a cell number's status
    /// from OPT_OUT back to OPT_IN via the privileged Admin API.
    /// </summary>
    ReOptIn = 3
}
