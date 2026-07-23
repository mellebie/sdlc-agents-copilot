using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Repositories;
using TCPA.Core.Services;

namespace TCPA.Core.Extensions;

/// <summary>
/// DI registration helpers for TCPA.Core — wire all repositories and services
/// from a single call in each host project's Program.cs.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all TCPA.Core services, repositories, and DbContext instances.
    ///
    /// Two DbContext registrations are created:
    ///   • Keyed "primary" — Scoped TcpaDbContext pointing at the write/primary endpoint.
    ///     Consumed by SqlOptOutStatusRepository and SqlAuditLogRepository via [FromKeyedServices("primary")].
    ///   • Keyed "replica" — Scoped TcpaDbContext pointing at the read-replica endpoint.
    ///     Consumed by the same repositories via [FromKeyedServices("replica")].
    ///   • Non-keyed (default) — Scoped TcpaDbContext pointing at the primary endpoint.
    ///     Consumed by ReOptInService, SqlCoolTextAccountRepository, and SqlSystemConfigRepository
    ///     which inject TcpaDbContext without a [FromKeyedServices] attribute.
    ///
    /// Reads connection strings from configuration:
    ///   ConnectionStrings:Primary     — required; SQL Server primary/write endpoint
    ///   ConnectionStrings:ReadReplica — optional; falls back to Primary if absent (dev/test)
    ///
    /// Call from Program.cs in each application project.
    /// </summary>
    public static IServiceCollection AddTcpaCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var primaryConnStr = configuration.GetConnectionString("Primary")
            ?? throw new InvalidOperationException("ConnectionStrings:Primary is required.");
        var replicaConnStr = configuration.GetConnectionString("ReadReplica")
            ?? primaryConnStr; // fall back to primary if no replica configured (dev/test)

        // Keyed "primary" — for repositories using [FromKeyedServices("primary")]
        services.AddKeyedScoped<TcpaDbContext>("primary", (_, _) =>
        {
            var opts = new DbContextOptionsBuilder<TcpaDbContext>()
                .UseSqlServer(primaryConnStr)
                .Options;
            return new TcpaDbContext(opts);
        });

        // Keyed "replica" — for repositories using [FromKeyedServices("replica")]
        services.AddKeyedScoped<TcpaDbContext>("replica", (_, _) =>
        {
            var opts = new DbContextOptionsBuilder<TcpaDbContext>()
                .UseSqlServer(replicaConnStr)
                .Options;
            return new TcpaDbContext(opts);
        });

        // Non-keyed primary alias — for services that inject TcpaDbContext directly
        // without a [FromKeyedServices] attribute (ReOptInService, read-only repos)
        services.AddDbContext<TcpaDbContext>(opt => opt.UseSqlServer(primaryConnStr));

        // Repositories
        services.AddScoped<IOptOutStatusRepository, SqlOptOutStatusRepository>();
        services.AddScoped<IAuditLogRepository, SqlAuditLogRepository>();
        services.AddScoped<ICoolTextAccountRepository, SqlCoolTextAccountRepository>();
        services.AddScoped<ISystemConfigRepository, SqlSystemConfigRepository>();

        // Domain services
        services.AddScoped<IReOptInService, ReOptInService>();
        services.AddSingleton<IKeywordDetectionService, KeywordDetectionService>();
        services.AddSingleton<IPhoneNumberHasher, PhoneNumberHasher>();

        return services;
    }
}
