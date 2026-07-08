using TCPA.Api.Models;

namespace TCPA.Api.Services.SmsProxy;

/// <summary>
/// TCPA compliance gate for outbound SMS messages submitted by upstream SCG applications.
///
/// This gate is the authoritative enforcement point that ensures no SMS message reaches
/// Cool Text/Twilio for a cell number that has opted out. It implements fail-closed behavior:
/// if the opt-out status cannot be confirmed, the message is blocked and a 503 is returned.
/// </summary>
public interface IOutboundSmsGate
{
    /// <summary>
    /// Evaluates an outbound SMS request against the TCPA opt-out database and either
    /// forwards it to Cool Text or suppresses it.
    /// </summary>
    /// <param name="request">Validated outbound SMS request from the upstream application.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OutboundGateResult"/> describing the compliance decision:
    /// forwarded, suppressed, or unregistered account.
    /// </returns>
    /// <exception cref="OutboundGateUnavailableException">
    /// Thrown when the TCPA opt-out database is unavailable and the opt-out status
    /// cannot be confirmed. The caller must return 503 Service Unavailable.
    /// This is the fail-closed behavior mandated by SPEC-001 and NFS-005.
    /// </exception>
    Task<OutboundGateResult> ProcessAsync(OutboundSmsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of the outbound SMS compliance gate evaluation.
/// </summary>
public sealed class OutboundGateResult
{
    /// <summary>The compliance decision outcome for this message.</summary>
    public OutboundGateDecision Decision { get; init; }

    /// <summary>
    /// Cool Text message identifier when the message was forwarded.
    /// Null when the message was suppressed or the account is unregistered.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>Creates a FORWARDED result with the Cool Text message identifier.</summary>
    public static OutboundGateResult Forwarded(string messageId) => new()
    {
        Decision = OutboundGateDecision.Forwarded,
        MessageId = messageId
    };

    /// <summary>Creates a SUPPRESSED result for a number with OPT_OUT status.</summary>
    public static OutboundGateResult Suppressed() => new()
    {
        Decision = OutboundGateDecision.Suppressed
    };

    /// <summary>Creates an UNREGISTERED_ACCOUNT result when the Cool Text account ID is not in the registry.</summary>
    public static OutboundGateResult UnregisteredAccount() => new()
    {
        Decision = OutboundGateDecision.UnregisteredAccount
    };
}

/// <summary>Outbound compliance gate decision outcomes.</summary>
public enum OutboundGateDecision
{
    /// <summary>Message passed the opt-out check and was forwarded to Cool Text.</summary>
    Forwarded,

    /// <summary>Message was blocked because the destination number has OPT_OUT status.</summary>
    Suppressed,

    /// <summary>The Cool Text account ID is not registered; message was passed through without enforcement.</summary>
    UnregisteredAccount
}

/// <summary>
/// Thrown when the outbound compliance gate cannot determine opt-out status due to database unavailability.
/// The controller must translate this into a 503 Service Unavailable response (fail-closed, NFS-005).
/// </summary>
public sealed class OutboundGateUnavailableException : Exception
{
    /// <summary>Initializes a new fail-closed exception with a mandatory message.</summary>
    public OutboundGateUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
