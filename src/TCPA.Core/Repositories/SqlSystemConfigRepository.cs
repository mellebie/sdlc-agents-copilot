using Microsoft.EntityFrameworkCore;
using TCPA.Core.Data;
using TCPA.Core.Exceptions;
using TCPA.Core.Interfaces;

namespace TCPA.Core.Repositories;

public class SqlSystemConfigRepository : ISystemConfigRepository
{
    private readonly TcpaDbContext _readCtx;

    public SqlSystemConfigRepository(TcpaDbContext readContext) => _readCtx = readContext;

    public async Task<string?> GetValueAsync(string key, CancellationToken ct)
    {
        var config = await _readCtx.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, ct);
        return string.IsNullOrWhiteSpace(config?.Value) ? null : config.Value;
    }

    public async Task<string> GetRequiredValueAsync(string key, CancellationToken ct)
    {
        var value = await GetValueAsync(key, ct);
        if (value is null)
            throw new ConfigurationException(key);
        return value;
    }
}
