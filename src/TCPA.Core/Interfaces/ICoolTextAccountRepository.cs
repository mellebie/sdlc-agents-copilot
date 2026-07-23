using TCPA.Core.Models;

namespace TCPA.Core.Interfaces;

public interface ICoolTextAccountRepository
{
    /// <summary>Returns null if account number is not registered. Callers must return 400 on null.</summary>
    Task<CoolTextAccount?> GetByAccountNumberAsync(string accountNumber, CancellationToken ct);
}
