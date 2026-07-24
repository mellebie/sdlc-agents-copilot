using TCPA.OutboundDispatcher.Messaging;

namespace TCPA.OutboundDispatcher.Services;

/// <summary>
/// Result of the outbound gate evaluation.
/// </summary>
/// <param name="IsAllowed">True if the message may proceed to send; false if suppressed.</param>
/// <param name="SuppressReason">
/// Populated when <see cref="IsAllowed"/> is false.
/// Values: <c>"opt_out"</c> | <c>"quiet_hours"</c>.
/// </param>
public record GateResult(bool IsAllowed, string? SuppressReason);

public interface IOutboundGateService
{
    /// <summary>
    /// Evaluates the outbound gate for the given message:
    /// 1. Checks opt-out status — if opted-out, writes <c>OutboundSuppressed</c> audit and returns suppressed.
    /// 2. Checks TCPA quiet hours (8 AM – 9 PM UTC conservative) — if outside window, suppresses.
    /// If both checks pass, returns <see cref="GateResult.IsAllowed"/> = true.
    /// Never throws — errors propagate to the caller (worker handles retry/poison pill).
    /// </summary>
    Task<GateResult> EvaluateAsync(OutboundMessageEvent @event, CancellationToken ct);
}
