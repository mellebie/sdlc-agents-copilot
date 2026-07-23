namespace TCPA.Core.Interfaces;

public interface ISystemConfigRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken ct);

    /// <summary>Throws <see cref="ConfigurationException"/> if key is missing or empty.</summary>
    Task<string> GetRequiredValueAsync(string key, CancellationToken ct);
}
