using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.Infrastructure.Configuration;

/// <summary>
/// Caching implementation of <see cref="IApplicationRegistryService"/>.
///
/// Wraps database reads with an in-memory cache (TTL controlled by
/// <see cref="ApplicationRegistryOptions.CacheTtlMinutes"/>, default 5 minutes).
/// The cache is primed at application startup by <see cref="ApplicationRegistryStartupService"/>
/// before any request traffic is accepted.
///
/// <para>
/// DESIGN: The cache maps Cool Text account numbers to their <see cref="ApplicationRegistryEntry"/>
/// values. Inactive registrations are explicitly excluded from the cache (they are never
/// stored). A cache lookup that returns null means "unregistered or inactive" — callers
/// must not distinguish between the two cases (BR-063, SPEC-014).
/// </para>
///
/// <para>
/// On a cache miss after TTL expiry, a warning is logged and the database is queried directly.
/// The result is re-cached with a fresh TTL. Under normal operation the cache hit rate
/// should be near 100% (registry is near-static data — 5 applications).
/// </para>
///
/// <para>
/// THREAD SAFETY: <see cref="IMemoryCache"/> is thread-safe for concurrent reads.
/// Cache population uses a lock-free "last writer wins" approach — the cost of a brief
/// window where multiple cache misses query the database simultaneously is negligible
/// for a 5-application dataset.
/// </para>
/// </summary>
public sealed class ApplicationRegistryService : IApplicationRegistryService
{
    private const string AllActiveEntriesCacheKey = "AppRegistry:AllActive";

    private readonly TcpaDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApplicationRegistryService> _logger;
    private readonly ApplicationRegistryOptions _options;

    /// <summary>
    /// Initializes the caching application registry service.
    /// </summary>
    /// <param name="dbContext">EF Core database context for fallback database reads.</param>
    /// <param name="cache">In-memory cache for storing registry entries.</param>
    /// <param name="logger">Structured logger for cache miss warnings and errors.</param>
    /// <param name="options">Configuration options including the cache TTL.</param>
    public ApplicationRegistryService(
        TcpaDbContext dbContext,
        IMemoryCache cache,
        ILogger<ApplicationRegistryService> logger,
        IOptions<ApplicationRegistryOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task<ApplicationRegistryEntry?> GetByAccountNumberAsync(
        string coolTextAccountNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(coolTextAccountNumber))
        {
            _logger.LogDebug(
                "ApplicationRegistryService: Lookup called with null or empty account number; returning null.");
            return null;
        }

        string cacheKey = BuildAccountCacheKey(coolTextAccountNumber);

        if (_cache.TryGetValue(cacheKey, out ApplicationRegistryEntry? cached))
        {
            return cached; // May be null — a null entry cached means "known unregistered/inactive"
        }

        // Cache miss — log at Warning because this should be rare (near-static dataset).
        _logger.LogWarning(
            "ApplicationRegistryService: Cache miss for account number. Falling back to database. " +
            "This is unexpected after startup cache priming. {CacheKey}", cacheKey);

        ApplicationRegistryEntry? entry = await LoadFromDatabaseAsync(coolTextAccountNumber, cancellationToken);

        // Cache the result (including null) to prevent repeat DB hits for unknown accounts.
        // A null entry is cached for a shorter duration to allow legitimate re-registrations
        // to become visible sooner.
        MemoryCacheEntryOptions entryOptions = entry is not null
            ? BuildCacheEntryOptions(_options.CacheTtlMinutes)
            : BuildCacheEntryOptions(1); // 1 minute TTL for negative (not-found) entries

        _cache.Set(cacheKey, entry, entryOptions);

        return entry;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ApplicationRegistryEntry>> GetAllActiveAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(AllActiveEntriesCacheKey, out IReadOnlyList<ApplicationRegistryEntry>? cached)
            && cached is not null)
        {
            return cached;
        }

        _logger.LogInformation(
            "ApplicationRegistryService: Loading all active application registrations from database.");

        List<ApplicationRegistryEntry> entries = await _dbContext.ApplicationRegistrations
            .AsNoTracking()
            .Where(ar => ar.IsActive)
            .Select(ar => new ApplicationRegistryEntry
            {
                CoolTextAccountNumber = ar.CoolTextAccountNumber,
                ApplicationName = ar.ApplicationName,
                CallbackUrl = ar.CallbackUrl,
                IsActive = ar.IsActive,
                OnboardedDate = ar.OnboardedDate
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "ApplicationRegistryService: Loaded {Count} active registrations from database.",
            entries.Count);

        _cache.Set(AllActiveEntriesCacheKey, (IReadOnlyList<ApplicationRegistryEntry>)entries,
            BuildCacheEntryOptions(_options.CacheTtlMinutes));

        // Also populate individual account-number keys from this bulk load so that
        // single lookups hit the cache without requiring a separate per-key DB query.
        foreach (ApplicationRegistryEntry entry in entries)
        {
            string accountCacheKey = BuildAccountCacheKey(entry.CoolTextAccountNumber);
            _cache.Set(accountCacheKey, entry, BuildCacheEntryOptions(_options.CacheTtlMinutes));
        }

        return entries;
    }

    private async Task<ApplicationRegistryEntry?> LoadFromDatabaseAsync(
        string coolTextAccountNumber,
        CancellationToken cancellationToken)
    {
        Domain.ApplicationRegistration? registration = await _dbContext.ApplicationRegistrations
            .AsNoTracking()
            .Where(ar => ar.CoolTextAccountNumber == coolTextAccountNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            _logger.LogDebug(
                "ApplicationRegistryService: Account number not found in database. " +
                "Treating as unregistered. {CacheKey}", BuildAccountCacheKey(coolTextAccountNumber));
            return null;
        }

        if (!registration.IsActive)
        {
            _logger.LogInformation(
                "ApplicationRegistryService: Account number found in registry but IsActive=false. " +
                "Application {ApplicationName} is treating as unregistered (BR-063).",
                registration.ApplicationName);
            return null;
        }

        return new ApplicationRegistryEntry
        {
            CoolTextAccountNumber = registration.CoolTextAccountNumber,
            ApplicationName = registration.ApplicationName,
            CallbackUrl = registration.CallbackUrl,
            IsActive = registration.IsActive,
            OnboardedDate = registration.OnboardedDate
        };
    }

    private static string BuildAccountCacheKey(string coolTextAccountNumber) =>
        $"AppRegistry:Account:{coolTextAccountNumber}";

    private static MemoryCacheEntryOptions BuildCacheEntryOptions(int ttlMinutes) =>
        new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes),
            Priority = CacheItemPriority.Normal
        };
}
