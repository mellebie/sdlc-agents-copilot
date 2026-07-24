using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.OutboundDispatcher.Messaging;

namespace TCPA.OutboundDispatcher.Services;

/// <summary>
/// Evaluates two TCPA compliance gates before an outbound SMS is sent:
/// 1. Opt-out check — recipient must not be on the opt-out list (SPEC-006, BR-020).
/// 2. Quiet hours check — current UTC time must be between 8 AM and 9 PM (TCPA §227(b)(1)(A)(iii)).
///    When recipient timezone is unknown, UTC is applied conservatively.
///
/// A suppressed message results in an <see cref="AuditEventType.OutboundSuppressed"/> audit entry.
/// An allowed message results in <see cref="GateResult.IsAllowed"/> = true with no audit write.
///
/// Exposed <c>internal</c> <see cref="EvaluateAsync_WithClock"/> overload allows tests to inject
/// a specific clock time without requiring a real-time dependency.
/// </summary>
public class OutboundGateService : IOutboundGateService
{
    private readonly TcpaDbContext _ctx;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ILogger<OutboundGateService> _logger;

    /// <summary>Initializes a new instance of <see cref="OutboundGateService"/>.</summary>
    public OutboundGateService(
        [FromKeyedServices("primary")] TcpaDbContext ctx,
        IOptOutStatusRepository statusRepo,
        IAuditLogRepository auditRepo,
        IPhoneNumberHasher hasher,
        ILogger<OutboundGateService> logger)
    {
        _ctx = ctx;
        _statusRepo = statusRepo;
        _auditRepo = auditRepo;
        _hasher = hasher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<GateResult> EvaluateAsync(OutboundMessageEvent @event, CancellationToken ct)
        => EvaluateAsync_WithClock(@event, DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// Public overload with injectable clock. Tests pass a specific <see cref="DateTimeOffset"/>
    /// to exercise boundary conditions without waiting for real clock transitions.
    /// </summary>
    internal async Task<GateResult> EvaluateAsync_WithClock(
        OutboundMessageEvent @event, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // Gate 1: Opt-out status — check before quiet hours (opt-out takes precedence)
        var isOptedOut = await _statusRepo.IsOptedOutAsync(@event.ToNumber, ct);
        if (isOptedOut)
        {
            _logger.LogInformation(
                "Outbound message suppressed: opt-out. PhoneHash={PhoneHash} MessageId={MessageId}",
                _hasher.Hash(@event.ToNumber), @event.MessageId);
            await WriteSuppressedAuditAsync(@event, "opt_out", ct);
            return new GateResult(false, "opt_out");
        }

        // Gate 2: TCPA quiet hours — 8 AM to 9 PM UTC (conservative when timezone unknown)
        if (!IsWithinTcpaHours(nowUtc))
        {
            _logger.LogInformation(
                "Outbound message suppressed: quiet hours. PhoneHash={PhoneHash} MessageId={MessageId} UtcHour={UtcHour}",
                _hasher.Hash(@event.ToNumber), @event.MessageId, nowUtc.Hour);
            await WriteSuppressedAuditAsync(@event, "quiet_hours", ct);
            return new GateResult(false, "quiet_hours");
        }

        return new GateResult(true, null);
    }

    /// <summary>
    /// Returns true if the given UTC time falls within the TCPA-allowed sending window
    /// (8:00 AM inclusive to 9:00 PM exclusive, in UTC).
    /// UTC is applied as the conservative default when recipient timezone is unknown.
    /// </summary>
    private static bool IsWithinTcpaHours(DateTimeOffset nowUtc)
    {
        int hourUtc = nowUtc.Hour;
        return hourUtc >= 8 && hourUtc < 21;
    }

    private async Task WriteSuppressedAuditAsync(
        OutboundMessageEvent @event, string reason, CancellationToken ct)
    {
        _auditRepo.Write(new AuditLog
        {
            EventType = AuditEventType.OutboundSuppressed,
            PhoneNumber = @event.ToNumber,
            OccurredAt = DateTime.UtcNow,
            ApplicationId = @event.ApplicationId,
            MessageId = @event.MessageId,
            Details = JsonSerializer.Serialize(new
            {
                reason,
                coolTextAccountNumber = @event.CoolTextAccountNumber
            })
        });
        await _ctx.SaveChangesAsync(ct);
    }
}
