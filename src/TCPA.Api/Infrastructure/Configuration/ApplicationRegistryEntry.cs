namespace TCPA.Api.Infrastructure.Configuration;

/// <summary>
/// Immutable value object representing the result of a successful application registry lookup.
/// Returned by <see cref="IApplicationRegistryService"/> when a Cool Text account ID resolves
/// to a registered, active SCG application.
///
/// <para>
/// All properties are populated from the <c>ApplicationRegistrations</c> database table
/// at the time of cache prime (startup) or cache refresh (TTL expiry). The values
/// reflect the state of the registry at the last refresh — they are not live database reads
/// per request, by design (ADR: in-memory cache with 5-minute TTL, TASK-003).
/// </para>
/// </summary>
public sealed class ApplicationRegistryEntry
{
    /// <summary>
    /// The Cool Text account number that identifies this application.
    /// This is the lookup key: every inbound/outbound message carries this value and
    /// the registry resolves which SCG application it belongs to.
    /// </summary>
    public required string CoolTextAccountNumber { get; init; }

    /// <summary>
    /// Human-readable SCG application name. Used in audit log entries, compliance reports,
    /// and operational logs. Examples: "GCMA", "KMI Active", "ARM/Construction Portal".
    /// </summary>
    public required string ApplicationName { get; init; }

    /// <summary>
    /// The HTTPS callback URL to which non-opt-out inbound SMS replies are forwarded
    /// for this application. Always starts with "https://".
    /// </summary>
    public required string CallbackUrl { get; init; }

    /// <summary>
    /// Whether this application is actively participating in TCPA enforcement.
    /// Inactive applications are treated as unregistered by the compliance gate and
    /// by the inbound router (BR-063).
    ///
    /// This flag is checked at cache-prime time; the cache only stores active entries.
    /// A lookup for an inactive application's Cool Text account returns null from the service.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// The date this application was onboarded into the TCPA system.
    /// Informational — used in audit trail context.
    /// </summary>
    public DateOnly OnboardedDate { get; init; }
}
