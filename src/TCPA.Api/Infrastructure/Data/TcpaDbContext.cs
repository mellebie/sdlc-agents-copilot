using Microsoft.EntityFrameworkCore;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data.EntityConfigurations;

namespace TCPA.Api.Infrastructure.Data;

/// <summary>
/// Primary EF Core database context for the TCPA Compliance API.
/// Covers the operational opt-out status store, the application registry,
/// the audit log, and the SMS message operational log.
///
/// <para>
/// ALWAYS ENCRYPTED: Cell phone number columns (<c>CellPhoneNumber</c> on
/// <see cref="CellNumberOptOutRecord"/> and <see cref="AuditLogEntry"/>) are configured
/// for Azure SQL Always Encrypted (deterministic AES-256). The connection string must include
/// <c>Column Encryption Setting=Enabled</c> and the application must have access to the
/// Column Master Key in Azure Key Vault (TASK-061, ADR-003).
/// </para>
///
/// <para>
/// AUDIT LOG IMMUTABILITY: <see cref="AuditLogEntries"/> is append-only. The EF Core
/// configuration for this entity intentionally omits update/delete mappings at the
/// repository layer. A database DDL trigger additionally rejects any UPDATE or DELETE
/// at the SQL layer (TASK-064, ADR-004).
/// </para>
/// </summary>
public sealed class TcpaDbContext : DbContext
{
    /// <summary>
    /// Initializes the TCPA database context with the supplied options.
    /// Options are configured via DI in <c>Program.cs</c> using the
    /// <c>ConnectionStrings:TcpaDatabase</c> configuration value.
    /// </summary>
    /// <param name="options">EF Core context options including the SQL Server connection string.</param>
    public TcpaDbContext(DbContextOptions<TcpaDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// The application registry: maps Cool Text account numbers to registered SCG applications.
    /// Near-static configuration data cached in memory at startup (TASK-003).
    /// </summary>
    public DbSet<ApplicationRegistration> ApplicationRegistrations => Set<ApplicationRegistration>();

    /// <summary>
    /// Authoritative opt-out status store: current OPT_IN/OPT_OUT state per cell number.
    /// Read on every outbound SMS compliance gate check. Cell number column is PII-encrypted.
    /// </summary>
    public DbSet<CellNumberOptOutRecord> OptOutRecords => Set<CellNumberOptOutRecord>();

    /// <summary>
    /// Immutable compliance audit log: records every opt-out, blocked outbound attempt,
    /// and re-opt-in event. Append-only — no updates or deletes permitted.
    /// Enforced by DDL trigger and application-layer repository pattern.
    /// </summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>
    /// Operational SMS message log: telemetry for individual messages processed by the proxy.
    /// Used for compliance reporting projections (TASK-040).
    /// </summary>
    public DbSet<SmsMessageLog> SmsMessageLogs => Set<SmsMessageLog>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ApplicationRegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new CellNumberOptOutRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new SmsMessageLogConfiguration());
    }
}
