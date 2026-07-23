using System.Text.Json;
using System.Text.Json.Serialization;
using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>
/// Holds the live, editable settings and persists them to settings.json.
/// Everything (GitService, the background workers, the notification channels)
/// reads from here, so changes saved from the UI take effect immediately with
/// no restart and no editing of backend files.
///
/// Settings are swapped atomically by reference on update, so any component that
/// grabs Current sees a fully-consistent snapshot.
/// </summary>
public class AppSettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private AppSettings _current;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettingsStore(AppPaths paths, IConfiguration config)
    {
        _path = paths.SettingsFile;
        _current = LoadOrSeed(config);
    }

    public AppSettings Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Persist new settings.</summary>
    public AppSettings Update(AppSettings incoming)
    {
        lock (_lock)
        {
            incoming.Git ??= new GitRepositoryConfig();
            _current = incoming;
            Save(_current);
            return _current;
        }
    }

    /// <summary>A copy safe to return to the UI.</summary>
    public AppSettings ForDisplay() => Clone(Current);

    private AppSettings LoadOrSeed(IConfiguration config)
    {
        if (File.Exists(_path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _json);
                if (loaded != null) return Normalize(loaded);
            }
            catch { /* fall through to seed */ }
        }

        // First run: seed from appsettings.json so any existing config still applies.
        var seed = new AppSettings();
        config.GetSection("Git").Bind(seed.Git);
        seed = Normalize(seed);
        Save(seed);
        return seed;
    }

    private static AppSettings Normalize(AppSettings s)
    {
        s.Git ??= new GitRepositoryConfig();
        if (string.IsNullOrWhiteSpace(s.Git.RemoteName)) s.Git.RemoteName = "origin";
        if (s.Git.FetchIntervalSeconds <= 0) s.Git.FetchIntervalSeconds = 60;
        return s;
    }

    private void Save(AppSettings s)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(s, _json));
    }

    private static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, _json), _json)!;
}
