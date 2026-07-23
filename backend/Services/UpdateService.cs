using System.Reflection;
using System.Text.Json;

namespace BranchMerger.Api.Services;

public class UpdateInfo
{
    public bool Enabled { get; set; }
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string? LatestVersion { get; set; }
    public string? Url { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Checks GitHub Releases for a newer version. The app carries its own version
/// (from the assembly, or UpdateCheck:CurrentVersion) and compares it to the
/// latest published release tag of the configured repo. Results are cached to
/// stay well under GitHub's unauthenticated rate limit.
/// </summary>
public class UpdateService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<UpdateService> _log;
    private readonly string _repo;
    private readonly string _current;
    private readonly bool _enabled;

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _cache;
    private DateTime _cacheUntil = DateTime.MinValue;

    public UpdateService(IConfiguration config, IHttpClientFactory http, ILogger<UpdateService> log)
    {
        _http = http;
        _log = log;
        _repo = config["UpdateCheck:GitHubRepo"] ?? "";
        _enabled = (config.GetValue<bool?>("UpdateCheck:Enabled") ?? true) && !string.IsNullOrWhiteSpace(_repo);
        _current = config["UpdateCheck:CurrentVersion"]
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "1.0.0";
    }

    public async Task<UpdateInfo> GetAsync(bool force, CancellationToken ct = default)
    {
        if (!_enabled)
            return new UpdateInfo
            {
                Enabled = false, UpdateAvailable = false, CurrentVersion = _current,
                Message = "Update checks are off. Set UpdateCheck:GitHubRepo to \"owner/repo\"."
            };

        if (!force && _cache != null && DateTime.UtcNow < _cacheUntil) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _cache != null && DateTime.UtcNow < _cacheUntil) return _cache;
            _cache = await CheckAsync(ct);
            _cacheUntil = DateTime.UtcNow.Add(Ttl);
            return _cache;
        }
        finally { _gate.Release(); }
    }

    private async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        var info = new UpdateInfo { Enabled = true, CurrentVersion = _current };
        try
        {
            var client = _http.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{_repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("BranchMerger-UpdateCheck");   // GitHub requires a UA
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            var res = await client.SendAsync(req, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                info.Message = "No published releases yet.";
                return info;
            }
            if (!res.IsSuccessStatusCode)
            {
                info.Message = $"GitHub returned {(int)res.StatusCode}.";
                return info;
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            info.LatestVersion = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            info.Url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            info.UpdateAvailable = IsNewer(info.LatestVersion, _current);
            info.Message = info.UpdateAvailable
                ? $"Version {info.LatestVersion} is available."
                : "You're up to date.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed");
            info.Message = "Update check failed: " + ex.Message;
        }
        return info;
    }

    private static bool IsNewer(string? latest, string current)
    {
        var l = Clean(latest);
        var c = Clean(current);
        if (Version.TryParse(l, out var lv) && Version.TryParse(c, out var cv)) return lv > cv;
        return !string.IsNullOrEmpty(l) && l != c; // fallback for non-numeric tags
    }

    private static string Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        v = v.TrimStart('v', 'V');
        var dash = v.IndexOf('-');
        if (dash > 0) v = v[..dash];             // drop pre-release suffix
        return new string(v.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
    }
}
