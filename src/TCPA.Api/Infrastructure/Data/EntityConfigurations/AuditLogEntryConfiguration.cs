using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Api.Domain;

namespace TCPA.Api.Infrastructure.Data.EntityConfigurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AuditLogEntry"/> entity.
/// Maps to the <c>AuditLogEntries</c> table in the audit log schema.
///
/// <para>
/// IMMUTABILITY: This table is append-only. A SQL DDL trigger enforces immutability at the
/// database layer by rejecting any UPDATE or DELETE operation (TASK-064, ADR-004).
/// At the application layer, <c>IAuditLogRepository</c> exposes only Append methods.
/// No EF Core update tracking is needed for this entity — it is configured to not track changes.
/// </para>
///
/// <para>
/// PII: <c>CellPhoneNumber</c> is encrypted via Azure SQL Always Encrypted (deterministic
/// AES-256). See <see cref="CellNumberOptOutRecordConfiguration"/> for the full encryption
/// rationale. The same Always Encrypted setup applies here (TASK-061).
/// </para>
///
/// <para>
/// RETENTION: Records must be retained for 5 years from <c>EventTimestamp</c>.
/// Records older than 90 days are tiered to Azure Blob Storage WORM (TASK-066).
/// </para>
/// </summary>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");

        builder.HasKey(e => e.RecordId);

        builder.Property(e => e.RecordId)
            .HasColumnName("RecordId")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.EventType)
            .HasColumnName("EventType")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.EventTimestamp)
            .HasColumnName("EventTimestamp")
            .IsRequired();

        // PII column — Always Encrypted (deterministic AES-256) applied at infrastructure level.
        builder.Property(e => e.CellPhoneNumber)
            .HasColumnName("CellPhoneNumber")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.OriginatingCoolTextAccountId)
            .HasColumnName("OriginatingCoolTextAccountId")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.OriginatingApplicationName)
            .HasColumnName("OriginatingApplicationName")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.OptOutKeywordReceived)
            .HasColumnName("OptOutKeywordReceived")
            .HasMaxLength(50)
            .IsRequired(false);

        // MessageBody is PII-adjacent. Stored via Azure SQL TDE at minimum (ADR-003).
        // Must not appear in production log output (BR-069).
        builder.Property(e => e.MessageBody)
            .HasColumnName("MessageBody")
            .IsRequired(false);

        builder.Property(e => e.SystemResponse)
            .HasColumnName("SystemResponse")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ConfirmationSmsSentStatus)
            .HasColumnName("ConfirmationSmsSentStatus")
            .HasConversion<string?>()
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(e => e.ConfirmationSmsTimestamp)
            .HasColumnName("ConfirmationSmsTimestamp")
            .IsRequired(false);

        builder.Property(e => e.SuppressionReason)
            .HasColumnName("SuppressionReason")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(e => e.AgentUserId)
            .HasColumnName("AgentUserId")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(e => e.Reason)
            .HasColumnName("Reason")
            .IsRequired(false);

        builder.Property(e => e.TicketReference)
            .HasColumnName("TicketReference")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(e => e.PreviousStatus)
            .HasColumnName("PreviousStatus")
            .HasConversion<string?>()
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired();

        // Index on EventTimestamp to support time-range compliance reporting queries.
        builder.HasIndex(e => e.EventTimestamp)
            .HasDatabaseName("IX_AuditLogEntries_EventTimestamp");

        // Index on OriginatingApplicationName to support per-application report filtering.
        builder.HasIndex(e => e.OriginatingApplicationName)
            .HasDatabaseName("IX_AuditLogEntries_ApplicationName");

        // Composite index for the reporting projection job (TASK-040):
        // filters by event type and event timestamp together.
        builder.HasIndex(e => new { e.EventType, e.EventTimestamp })
            .HasDatabaseName("IX_AuditLogEntries_EventType_EventTimestamp");
    }
}
