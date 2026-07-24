using TCPA.Core.Models;

namespace TCPA.Core.Interfaces;

public interface ICoolTextApiClient
{
    /// <summary>
    /// Sends an SMS via the Cool Text gateway.
    /// Returns a success result with provider MessageId on 2xx.
    /// Returns a failure result on 4xx/5xx — the caller decides whether to retry.
    /// Throws on network / timeout — the caller handles retries.
    /// </summary>
    Task<CoolTextSendResult> SendSmsAsync(
        string toPhoneNumber,
        string fromAccountNumber,
        string body,
        CancellationToken ct);
}
