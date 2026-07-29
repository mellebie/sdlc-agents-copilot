using ConsentService.Models;

namespace ConsentService.Repositories;

public interface IConsentTransitionRepository
{
    Task<ConsentTransitionRecord?> FindByPhoneWithinWindowAsync(string customerPhoneNumber, TimeSpan window, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task SaveAsync(ConsentTransitionRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsentTransitionRecord>> GetAllAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryConsentTransitionRepository : IConsentTransitionRepository
{
    private readonly List<ConsentTransitionRecord> _records = [];

    public Task<ConsentTransitionRecord?> FindByPhoneWithinWindowAsync(string customerPhoneNumber, TimeSpan window, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var candidate = _records
            .Where(record => string.Equals(record.CustomerPhoneNumber, customerPhoneNumber, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.RequestedAtUtc)
            .FirstOrDefault(record => nowUtc - record.RequestedAtUtc <= window);

        return Task.FromResult<ConsentTransitionRecord?>(candidate);
    }

    public Task SaveAsync(ConsentTransitionRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConsentTransitionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ConsentTransitionRecord>>(_records.ToList());
    }
}
