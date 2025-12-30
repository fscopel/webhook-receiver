using System.Collections.Concurrent;
using WebhookReceiver.Models;

namespace WebhookReceiver.Services;

public class WebhookStore
{
    private readonly ConcurrentDictionary<string, WebhookEntry> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    public void Add(WebhookEntry entry)
    {
        _entries[entry.Id] = entry;
    }

    public IEnumerable<WebhookEntry> GetAll()
    {
        return _entries.Values
            .OrderByDescending(e => e.ReceivedAt)
            .ToList();
    }

    public WebhookEntry? Get(string id)
    {
        return _entries.TryGetValue(id, out var entry) ? entry : null;
    }

    public bool Delete(string id)
    {
        return _entries.TryRemove(id, out _);
    }

    public int Clear()
    {
        var count = _entries.Count;
        _entries.Clear();
        return count;
    }

    public int CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _ttl;
        var expiredKeys = _entries
            .Where(kvp => kvp.Value.ReceivedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _entries.TryRemove(key, out _);
        }

        return expiredKeys.Count;
    }

    public int Count => _entries.Count;
}

