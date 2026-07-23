using Microsoft.EntityFrameworkCore;
using TCPA.Core.Models;

namespace TCPA.Core.Data;

public class TcpaDbContext : DbContext
{
    public TcpaDbContext(DbContextOptions<TcpaDbContext> options) : base(options) { }

    public DbSet<OptOutStatus> OptOutStatuses => Set<OptOutStatus>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CoolTextAccount> CoolTextAccounts => Set<CoolTextAccount>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TcpaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
