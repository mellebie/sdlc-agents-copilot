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

        // Index on ProcessedAt for efficient time-based queries
        builder.HasIndex(x => x.ProcessedAt);
    }
}
