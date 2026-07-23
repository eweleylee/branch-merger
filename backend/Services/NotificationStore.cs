using System.Collections.Concurrent;
using System.Text.Json;
using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>
/// Holds the in-app notification feed. Thread-safe, capped, and persisted to JSON
/// so alerts survive a restart. This is the "always-on" channel.
/// </summary>
public class NotificationStore
{
    private const int MaxItems = 200;
    private readonly ConcurrentQueue<Notification> _items = new();
    private readonly string _path;
    private readonly object _fileLock = new();
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public NotificationStore(AppPaths paths)
    {
        _path = paths.NotificationsFile;
        Load();
    }

    public void Add(Notification n)
    {
        _items.Enqueue(n);
        while (_items.Count > MaxItems && _items.TryDequeue(out _)) { }
        Save();
    }

    public IReadOnlyList<Notification> GetAll() =>
        _items.OrderByDescending(n => n.CreatedUtc).ToList();

    public int UnreadCount() => _items.Count(n => !n.Read);

    public void MarkRead(Guid id)
    {
        var n = _items.FirstOrDefault(x => x.Id == id);
        if (n != null) { n.Read = true; Save(); }
    }

    public void MarkAllRead()
    {
        foreach (var n in _items) n.Read = true;
        Save();
    }

    public void Clear()
    {
        while (_items.TryDequeue(out _)) { }
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonSerializer.Deserialize<List<Notification>>(File.ReadAllText(_path), _json);
            if (list != null)
                foreach (var n in list.OrderBy(x => x.CreatedUtc)) _items.Enqueue(n);
        }
        catch { /* start empty on corrupt file */ }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_items.ToList(), _json));
        }
    }
}
