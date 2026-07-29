namespace ConsentService.Security;

public interface IReplayProtectionService
{
    bool IsReplay(string requestId, DateTimeOffset nowUtc);
    void Remember(string requestId, DateTimeOffset nowUtc);
}

public sealed class ReplayProtectionService : IReplayProtectionService
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;

    public ReplayProtectionService(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public bool IsReplay(string requestId, DateTimeOffset nowUtc)
    {
        Expire(nowUtc);
        return _seen.ContainsKey(requestId);
    }

    public void Remember(string requestId, DateTimeOffset nowUtc)
    {
        Expire(nowUtc);
        _seen[requestId] = nowUtc;
    }

    private void Expire(DateTimeOffset nowUtc)
    {
        var expired = _seen.Where(x => nowUtc - x.Value > _ttl).Select(x => x.Key).ToList();
        foreach (var key in expired)
        {
            _seen.Remove(key);
        }
    }
}
