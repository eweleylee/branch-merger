using System.Collections.Concurrent;
using System.Text.Json;
using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>
/// In-memory schedule store persisted to a JSON file so schedules survive restarts.
/// </summary>
public class ScheduleStore
{
    private readonly ConcurrentDictionary<Guid, MergeSchedule> _items = new();
    private readonly string _path;
    private readonly object _fileLock = new();
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ScheduleStore(AppPaths paths)
    {
        _path = paths.SchedulesFile;
        Load();
    }

    public IReadOnlyCollection<MergeSchedule> GetAll() =>
        _items.Values.OrderByDescending(s => s.CreatedUtc).ToList();

    public MergeSchedule? Get(Guid id) => _items.TryGetValue(id, out var s) ? s : null;

    public void Upsert(MergeSchedule schedule)
    {
        _items[schedule.Id] = schedule;
        Save();
    }

    public bool Remove(Guid id)
    {
        var removed = _items.TryRemove(id, out _);
        if (removed) Save();
        return removed;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<MergeSchedule>>(json, _json);
            if (list != null)
                foreach (var s in list) _items[s.Id] = s;
        }
        catch { /* start empty if the file is corrupt */ }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            var json = JsonSerializer.Serialize(_items.Values.ToList(), _json);
            File.WriteAllText(_path, json);
        }
    }
}
