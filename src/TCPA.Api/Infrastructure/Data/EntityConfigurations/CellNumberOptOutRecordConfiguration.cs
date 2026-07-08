using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Api.Domain;

namespace TCPA.Api.Infrastructure.Data.EntityConfigurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="CellNumberOptOutRecord"/> entity.
/// Maps to the <c>CellNumberOptOutRecords</c> table in the operational database.
///
/// <para>
/// CRITICAL PII NOTE: The <c>CellPhoneNumber</c> column stores cell phone numbers and is
/// designated for Azure SQL Always Encrypted with deterministic AES-256 encryption (ADR-003,
/// TASK-061, NFS-007). The migration creates the column as <c>nvarchar(20)</c>; the Always
/// Encrypted column configuration is applied by the DBA/platform team using Azure Key Vault
/// Column Master Key and Column Encryption Key provisioning (TASK-061).
///
/// Deterministic encryption supports indexed equality lookups, which is required for the
/// compliance gate read path (SPEC-006 / NFS-002: sub-50ms p99 lookup at the compliance gate).
/// </para>
///
/// <para>
/// The unique index on <c>CellPhoneNumber</c> ensures exactly one authoritative status record
/// per cell number in the system.
/// </para>
/// </summary>
internal sealed class CellNumberOptOutRecordConfiguration : IEntityTypeConfiguration<CellNumberOptOutRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CellNumberOptOutRecord> builder)
    {
        builder.ToTable("CellNumberOptOutRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        // PII column — Always Encrypted applied at infrastructure level.
        // Column Encryption Setting=Enabled must be present in the connection string.
        builder.Property(r => r.CellPhoneNumber)
            .HasColumnName("CellPhoneNumber")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("Status")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.LastOptOutTimestamp)
            .HasColumnName("LastOptOutTimestamp")
            .IsRequired(false);

        builder.Property(r => r.LastOptInTimestamp)
            .HasColumnName("LastOptInTimestamp")
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .IsRequired();

        // Unique index on CellPhoneNumber — ensures one authoritative record per cell number.
        // With Always Encrypted (deterministic), equality lookups on this column work correctly
        // because deterministic encryption produces the same ciphertext for the same plaintext.
        // Range queries and LIKE on this column are NOT supported with Always Encrypted — this
        // is an accepted constraint per ADR-003 (the access pattern is point-lookup only).
        builder.HasIndex(r => r.CellPhoneNumber)
            .IsUnique()
            .HasDatabaseName("IX_CellNumberOptOutRecords_CellPhoneNumber");
    }
}
