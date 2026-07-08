namespace TCPA.Api.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed options for the Application Registry service.
/// Bound from the <c>ApplicationRegistry</c> section of application configuration.
/// </summary>
public sealed class ApplicationRegistryOptions
{
    /// <summary>
    /// Configuration section key for binding this options class.
    /// </summary>
    public const string SectionName = "ApplicationRegistry";

    /// <summary>
    /// Number of minutes before cached application registry entries expire and are
    /// re-fetched from the database on the next lookup.
    ///
    /// Default is 5 minutes. The registry is near-static data (5 applications);
    /// a 5-minute TTL ensures configuration changes are visible within minutes without
    /// requiring a service restart (TASK-003).
    ///
    /// Must be a positive integer. Values less than 1 are treated as 1.
    /// </summary>
    public int CacheTtlMinutes { get; init; } = 5;

    /// <summary>
    /// Startup validation configuration for the application registry.
    /// </summary>
    public StartupValidationOptions StartupValidation { get; init; } = new();
}

/// <summary>
/// Options controlling startup validation behavior for the application registry.
/// </summary>
public sealed class StartupValidationOptions
{
    /// <summary>
    /// The list of SCG application names expected to be present in the registry at startup.
    /// If any of these names are missing, a warning is logged at startup (TASK-050).
    ///
    /// Default: the five in-scope SCG applications per SPEC-014.
    /// </summary>
    public IReadOnlyList<string> RequiredApplicationNames { get; init; } = new[]
    {
        "BizTalk",
        "GCMA",
        "KMI Active",
        "ARM/Construction Portal",
        "CCB/My Account"
    };
}
