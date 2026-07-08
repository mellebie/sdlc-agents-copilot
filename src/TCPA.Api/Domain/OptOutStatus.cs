namespace TCPA.Api.Domain;

/// <summary>
/// Represents the opt-out/opt-in status for a cell phone number under TCPA compliance.
/// This enum drives the core compliance gate decision in the outbound SMS proxy.
/// </summary>
public enum OptOutStatus
{
    /// <summary>
    /// The cell number holder has actively opted in (or has no recorded opt-out history).
    /// Per BR-001, the default state for a number with no record is OPT_IN.
    /// </summary>
    OptIn = 0,

    /// <summary>
    /// The cell number holder has opted out of receiving SMS messages from the application.
    /// All outbound messages to OPT_OUT numbers are suppressed by the compliance gate.
    /// </summary>
    OptOut = 1
}
