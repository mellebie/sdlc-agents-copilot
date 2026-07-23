using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Core.Models;

namespace TCPA.Core.Models.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.ApplicationId).HasMaxLength(50);
        builder.Property(x => x.MessageId).HasMaxLength(100);
        builder.Property(x => x.AgentId).HasMaxLength(100);
        builder.Property(x => x.Details).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AnomalyFlag).HasDefaultValue(false);
        builder.HasIndex(x => new { x.PhoneNumber, x.OccurredAt });
    }
}
