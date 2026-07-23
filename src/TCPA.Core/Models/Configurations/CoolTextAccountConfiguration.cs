using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TCPA.Core.Models;

namespace TCPA.Core.Models.Configurations;

public class CoolTextAccountConfiguration : IEntityTypeConfiguration<CoolTextAccount>
{
    public void Configure(EntityTypeBuilder<CoolTextAccount> builder)
    {
        builder.ToTable("CoolTextAccount");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ApplicationId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ApplicationName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CallbackUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        // One Cool Text account → one Gas application (SPEC-015, CQ-001)
        builder.HasIndex(x => x.AccountNumber).IsUnique();
    }
}
