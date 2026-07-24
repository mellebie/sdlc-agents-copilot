using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Core.Models;

namespace TCPA.Core.Models.Configurations;

/// <summary>
/// EF Core configuration for ProcessedMessage entity.
/// Configures the idempotency store table with MessageId as the primary key.
/// </summary>
public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");
        builder.HasKey(x => x.MessageId);

        builder.Property(x => x.MessageId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.InternalId)
            .IsRequired();

        builder.Property(x => x.ResponseStatus)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .IsRequired();

        builder.Property(x => x.Endpoint)
            .HasMaxLength(20)
            .IsRequired();

        // Composite unique index — enforces that (MessageId, Endpoint) is globally unique.
        // FindAsync filters on both columns; this ensures database-level protection against
        // cross-endpoint collisions that would pass the application-level FindAsync check but
        // fail on insert, and is consistent with the DbUpdateException idempotency guard
        // in the controllers.
        builder.HasIndex(m => new { m.MessageId, m.Endpoint }).IsUnique();

        // Index on ProcessedAt for efficient time-based queries
        builder.HasIndex(x => x.ProcessedAt);
    }
}
