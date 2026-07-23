using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Core.Models;

namespace TCPA.Core.Models.Configurations;

public class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.ToTable("SystemConfig");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Value).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
