using ConsentService.Models;

namespace ConsentService.Repositories;

public interface IConsentStateRepository
{
    Task<ConsentStatus?> GetStatusAsync(string customerPhoneNumber, CancellationToken cancellationToken = default);
    Task SetStatusAsync(string customerPhoneNumber, ConsentStatus status, CancellationToken cancellationToken = default);
}

public sealed class InMemoryConsentStateRepository : IConsentStateRepository
{
    private readonly Dictionary<string, ConsentStatus> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task<ConsentStatus?> GetStatusAsync(string customerPhoneNumber, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_states.TryGetValue(customerPhoneNumber, out var status) ? (ConsentStatus?)status : null);
    }

    public Task SetStatusAsync(string customerPhoneNumber, ConsentStatus status, CancellationToken cancellationToken = default)
    {
        _states[customerPhoneNumber] = status;
        return Task.CompletedTask;
    }
}
