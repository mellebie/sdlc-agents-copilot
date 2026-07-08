using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Api.Domain;

namespace TCPA.Api.Infrastructure.Data.EntityConfigurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="ApplicationRegistration"/> entity.
/// Maps to the <c>ApplicationRegistrations</c> table in the operational database.
///
/// <para>
/// The <c>CoolTextAccountNumber</c> column has a unique index — it is the runtime lookup key
/// used on every inbound and outbound message to resolve the originating application.
/// </para>
///
/// <para>
/// Note: In production, the CoolTextAccountNumber column should be protected via Always
/// Encrypted at the infrastructure level (TASK-061). EF Core's column type is left as
/// nvarchar to allow the Always Encrypted configuration to be applied by the DBA/platform
/// team independently of this migration.
/// </para>
/// </summary>
internal sealed class ApplicationRegistrationConfiguration : IEntityTypeConfiguration<ApplicationRegistration>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ApplicationRegistration> builder)
    {
        builder.ToTable("ApplicationRegistrations");

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(ar => ar.CoolTextAccountNumber)
            .HasColumnName("CoolTextAccountNumber")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(ar => ar.ApplicationName)
            .HasColumnName("ApplicationName")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(ar => ar.CallbackUrl)
            .HasColumnName("CallbackUrl")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(ar => ar.IsActive)
            .HasColumnName("IsActive")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(ar => ar.OnboardedDate)
            .HasColumnName("OnboardedDate")
            .IsRequired();

        builder.Property(ar => ar.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired();

        builder.Property(ar => ar.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .IsRequired();

        // Unique index on CoolTextAccountNumber — the runtime lookup key.
        // All inbound and outbound message routing resolves the application via this key.
        builder.HasIndex(ar => ar.CoolTextAccountNumber)
            .IsUnique()
            .HasDatabaseName("IX_ApplicationRegistrations_CoolTextAccountNumber");
    }
}
