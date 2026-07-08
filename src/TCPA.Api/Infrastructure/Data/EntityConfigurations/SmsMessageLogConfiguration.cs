using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Api.Domain;

namespace TCPA.Api.Infrastructure.Data.EntityConfigurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SmsMessageLog"/> entity.
/// Maps to the <c>SmsMessageLogs</c> table in the operational database.
///
/// <para>
/// This table captures operational telemetry for messages processed by the TCPA proxy.
/// It feeds the compliance reporting projection database (TASK-040, SPEC-011, SPEC-012).
/// </para>
///
/// <para>
/// PII: <c>CellPhoneNumber</c> is encrypted via Azure SQL Always Encrypted (deterministic
/// AES-256) consistent with all other cell number columns in the system (TASK-061).
/// </para>
/// </summary>
internal sealed class SmsMessageLogConfiguration : IEntityTypeConfiguration<SmsMessageLog>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SmsMessageLog> builder)
    {
        builder.ToTable("SmsMessageLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        // PII column — Always Encrypted (deterministic AES-256) applied at infrastructure level.
        builder.Property(log => log.CellPhoneNumber)
            .HasColumnName("CellPhoneNumber")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(log => log.ApplicationName)
            .HasColumnName("ApplicationName")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(log => log.Direction)
            .HasColumnName("Direction")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // MessageContent excluded from production logs (BR-069).
        builder.Property(log => log.MessageContent)
            .HasColumnName("MessageContent")
            .IsRequired(false);

        builder.Property(log => log.Status)
            .HasColumnName("Status")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(log => log.Timestamp)
            .HasColumnName("Timestamp")
            .IsRequired();

        // Index for time-range reporting queries.
        builder.HasIndex(log => log.Timestamp)
            .HasDatabaseName("IX_SmsMessageLogs_Timestamp");

        // Index for per-application filtering in compliance reports.
        builder.HasIndex(log => log.ApplicationName)
            .HasDatabaseName("IX_SmsMessageLogs_ApplicationName");
    }
}
