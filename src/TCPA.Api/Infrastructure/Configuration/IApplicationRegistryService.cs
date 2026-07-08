namespace TCPA.Api.Infrastructure.Configuration;

/// <summary>
/// Provides runtime lookups of SCG application registrations by Cool Text account number.
/// This service is the single integration point for all components that need to resolve
/// a Cool Text account ID to an application (compliance gate, inbound router, audit log writer).
///
/// <para>
/// Implementations cache registry entries in memory with a 5-minute TTL to eliminate
/// per-request database reads for this near-static dataset (TASK-003, Architecture: Application
/// Registry component). The cache is primed at application startup via the startup hosted service.
/// </para>
///
/// <para>
/// BEHAVIOR CONTRACT:
/// - Returns <c>null</c> for any account ID that is not registered or is marked inactive.
/// - Inactive registrations are treated identically to unregistered ones (BR-063).
/// - Callers must treat a <c>null</c> return as "unregistered / no enforcement" (SPEC-014).
/// - This service never throws for an unknown account ID; it returns <c>null</c>.
/// </para>
/// </summary>
public interface IApplicationRegistryService
{
    /// <summary>
    /// Looks up an application registration by its Cool Text account number.
    ///
    /// Returns the <see cref="ApplicationRegistryEntry"/> for the application if it is
    /// registered and active. Returns <c>null</c> if:
    /// - The account number is not found in the registry, OR
    /// - The account's <c>IsActive</c> flag is false.
    ///
    /// The result is served from the in-memory cache (5-minute TTL). On a cache miss,
    /// the lookup falls through to the database and a warning is logged (TASK-003).
    /// </summary>
    /// <param name="coolTextAccountNumber">
    /// The Cool Text account number to look up. Must be non-null and non-empty;
    /// passing null or empty returns null without a database query.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for async database fallback.</param>
    /// <returns>
    /// The matching <see cref="ApplicationRegistryEntry"/>, or <c>null</c> if the account
    /// is unregistered or inactive.
    /// </returns>
    Task<ApplicationRegistryEntry?> GetByAccountNumberAsync(
        string coolTextAccountNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently registered and active application entries.
    /// Used by startup validation (TASK-004) and startup cache priming (TASK-003).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// All active application registrations. Returns an empty collection if none are configured.
    /// Never returns null.
    /// </returns>
    Task<IReadOnlyList<ApplicationRegistryEntry>> GetAllActiveAsync(
        CancellationToken cancellationToken = default);
}
