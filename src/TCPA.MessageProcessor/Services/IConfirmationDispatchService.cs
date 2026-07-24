namespace TCPA.MessageProcessor.Services;

public interface IConfirmationDispatchService
{
    /// <summary>
    /// Reads the opt-out message body from SystemConfig, sends it via ICoolTextApiClient with
    /// up to 3 retries (exponential 2s/4s/8s), writes ConfirmationDispatched or ConfirmationFailed
    /// audit entries, and writes SlaBreach if latency exceeds 60 seconds.
    /// Never throws — all errors are logged and recorded as ConfirmationFailed.
    /// </summary>
    Task DispatchConfirmationAsync(
        string phoneNumber,
        string coolTextAccountNumber,
        DateTimeOffset receivedAt,
        long auditRecordId,
        CancellationToken ct);
}
