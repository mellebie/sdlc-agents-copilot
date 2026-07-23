using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Core.Models;

namespace TCPA.Core.Models.Configurations;

public class OptOutStatusConfiguration : IEntityTypeConfiguration<OptOutStatus>
{
    public void Configure(EntityTypeBuilder<OptOutStatus> builder)
    {
        builder.ToTable("OptOutStatus");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EffectiveAt).IsRequired();
        builder.Property(x => x.AuditRecordId).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        // Unique index on PhoneNumber — one record per phone number
        builder.HasIndex(x => x.PhoneNumber).IsUnique();
    }
}
