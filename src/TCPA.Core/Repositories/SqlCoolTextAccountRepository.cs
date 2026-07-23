using Microsoft.EntityFrameworkCore;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;

namespace TCPA.Core.Repositories;

public class SqlCoolTextAccountRepository : ICoolTextAccountRepository
{
    private readonly TcpaDbContext _readCtx;

    public SqlCoolTextAccountRepository(TcpaDbContext readContext)
        => _readCtx = readContext;

    public async Task<CoolTextAccount?> GetByAccountNumberAsync(string accountNumber, CancellationToken ct)
        => await _readCtx.CoolTextAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AccountNumber == accountNumber && x.IsActive, ct);
}
