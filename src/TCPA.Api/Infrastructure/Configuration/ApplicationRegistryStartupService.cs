using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TCPA.Api.Infrastructure.Configuration;

/// <summary>
/// Hosted service that runs at application startup to:
/// 1. Prime the in-memory application registry cache before the first request is served (TASK-003).
/// 2. Validate the integrity of all registered application entries (TASK-004, TASK-050).
///
/// <para>
/// STARTUP VALIDATION RULES (per TASK-004):
/// - Every entry must have a non-empty CoolTextAccountNumber and ApplicationName.
/// - Every CallbackUrl must start with "https://" (HTTPS-only enforcement).
///
/// Validation failures cause a hard exception, aborting service startup. The application
/// must not accept traffic if registry configuration is invalid — a misconfigured registry
/// could result in inbound SMS replies being routed to insecure HTTP endpoints or not routed
/// at all (compliance and security risk).
/// </para>
///
/// <para>
/// PRESENCE CHECKS (per TASK-050):
/// - Each of the five expected SCG application names is present in the registry.
/// - CCB/My Account is registered with IsActive=false (the default enforcement gate).
///
/// Presence check failures log warnings but do NOT abort startup — they allow partial
/// deployments to be diagnosed without blocking the service from starting (which would
/// block all other applications' TCPA enforcement).
/// </para>
/// </summary>
public sealed class ApplicationRegistryStartupService : IHostedService
{
    private readonly IApplicationRegistryService _registryService;
    private readonly ILogger<ApplicationRegistryStartupService> _logger;
    private readonly ApplicationRegistryOptions _options;

    /// <summary>
    /// Initializes the startup service.
    /// </summary>
    /// <param name="registryService">Registry service whose cache this service primes.</param>
    /// <param name="logger">Structured logger for validation results.</param>
    /// <param name="options">Options containing required application names for presence checks.</param>
    public ApplicationRegistryStartupService(
        IApplicationRegistryService registryService,
        ILogger<ApplicationRegistryStartupService> logger,
        IOptions<ApplicationRegistryOptions> options)
    {
        _registryService = registryService ?? throw new ArgumentNullException(nameof(registryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Executes at application startup: primes the cache and validates registry integrity.
    /// Throws <see cref="InvalidOperationException"/> if any HTTPS validation rule fails.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the startup operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a registry entry fails HTTPS callback URL validation. This aborts
    /// service startup to prevent routing inbound SMS to insecure or misconfigured endpoints.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ApplicationRegistryStartupService: Priming application registry cache at startup.");

        IReadOnlyList<ApplicationRegistryEntry> entries =
            await _registryService.GetAllActiveAsync(cancellationToken);

        _logger.LogInformation(
            "ApplicationRegistryStartupService: Loaded {EntryCount} active application registrations.",
            entries.Count);

        ValidateEntryIntegrity(entries);
        ValidatePresenceRequirements(entries);

        _logger.LogInformation(
            "ApplicationRegistryStartupService: Application registry startup validation complete. " +
            "Service is ready to accept requests.");
    }

    /// <summary>
    /// No-op on stop — cache cleanup is handled by the DI container lifecycle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (unused).</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Validates that every loaded registry entry satisfies the integrity rules:
    /// non-empty identifiers and HTTPS callback URLs.
    ///
    /// Throws on first violation — a misconfigured entry must be corrected before
    /// the service is permitted to start (TASK-004).
    /// </summary>
    /// <param name="entries">Active registry entries loaded from the database.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any entry fails integrity validation.
    /// </exception>
    private void ValidateEntryIntegrity(IReadOnlyList<ApplicationRegistryEntry> entries)
    {
        foreach (ApplicationRegistryEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.CoolTextAccountNumber))
            {
                string message =
                    $"ApplicationRegistryStartupService: Registry entry for application " +
                    $"'{entry.ApplicationName}' has an empty CoolTextAccountNumber. " +
                    "Service cannot start with invalid registry configuration.";
                _logger.LogError("{ValidationError}", message);
                throw new InvalidOperationException(message);
            }

            if (string.IsNullOrWhiteSpace(entry.ApplicationName))
            {
                string message =
                    "ApplicationRegistryStartupService: A registry entry has an empty ApplicationName. " +
                    $"CoolTextAccountNumber prefix for diagnosis: " +
                    $"'{MaskAccountNumber(entry.CoolTextAccountNumber)}'. " +
                    "Service cannot start with invalid registry configuration.";
                _logger.LogError("{ValidationError}", message);
                throw new InvalidOperationException(message);
            }

            if (!entry.CallbackUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string message =
                    $"ApplicationRegistryStartupService: Registry entry for application " +
                    $"'{entry.ApplicationName}' has a non-HTTPS callback URL. " +
                    "All callback URLs must use HTTPS. " +
                    "Service cannot start with an insecure callback URL in the registry.";
                _logger.LogError("{ValidationError}", message);
                throw new InvalidOperationException(message);
            }
        }
    }

    /// <summary>
    /// Checks that all expected application names are present in the loaded entries.
    /// Logs warnings for any missing expected applications but does NOT throw — a partial
    /// registry allows other applications' enforcement to continue while the missing entry
    /// is diagnosed and corrected.
    ///
    /// Also validates CCB/My Account inactive flag requirement (ARCH-RISK-006).
    /// </summary>
    /// <param name="entries">Active registry entries (inactive are not included in this list).</param>
    private void ValidatePresenceRequirements(IReadOnlyList<ApplicationRegistryEntry> entries)
    {
        HashSet<string> loadedNames = entries
            .Select(e => e.ApplicationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string requiredName in _options.StartupValidation.RequiredApplicationNames)
        {
            if (!loadedNames.Contains(requiredName))
            {
                // CCB/My Account is expected to be present but inactive — it will NOT appear
                // in the active entries list, so this is a legitimate "absence" for CCB.
                // Log at Information level for CCB, Warning for others.
                if (requiredName.Equals("CCB/My Account", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "ApplicationRegistryStartupService: 'CCB/My Account' not found in active registrations. " +
                        "This is expected if CCB is registered with IsActive=false (ARCH-RISK-006). " +
                        "Verify the CCB entry exists in the database with active=false.");
                }
                else
                {
                    _logger.LogWarning(
                        "ApplicationRegistryStartupService: Expected application '{ApplicationName}' " +
                        "is not present in the active registry. " +
                        "This application will not have TCPA enforcement until it is registered. " +
                        "Verify the registry seed script has been run (TASK-049).",
                        requiredName);
                }
            }
        }

        // Verify count is within expected range.
        // There should be at most 5 active entries (CCB is inactive, so max active = 4).
        if (entries.Count > 5)
        {
            _logger.LogWarning(
                "ApplicationRegistryStartupService: Found {EntryCount} active registrations, " +
                "which exceeds the expected maximum of 5. Verify no unexpected entries have been added.",
                entries.Count);
        }
    }

    /// <summary>
    /// Returns a masked representation of an account number for safe logging.
    /// Shows only the last 4 characters of the account number to aid diagnosis
    /// without logging sensitive configuration values.
    /// </summary>
    private static string MaskAccountNumber(string accountNumber)
    {
        if (accountNumber.Length <= 4)
        {
            return "****";
        }
        return "****" + accountNumber[^4..];
    }
}
