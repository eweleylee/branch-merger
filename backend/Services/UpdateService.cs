using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace BranchMerger.Api.Services;

public class UpdateInfo
{
    public bool Enabled { get; set; }
    public bool UpdateAvailable { get; set; }
    /// <summary>True only when the app was installed via the Velopack installer,
    /// so it can download + apply the update in place (the "Update now" button).</summary>
    public bool CanSelfUpdate { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string? LatestVersion { get; set; }
    public string? Url { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Checks GitHub Releases for a newer version and, when the app was installed via
/// the Velopack installer, can download and apply the update in place.
///
/// Two modes:
///   • Installed (Velopack)  → uses Velopack's <see cref="UpdateManager"/> against the
///     GitHub release feed. Can self-update (download + apply + restart).
///   • Not installed (dev / portable build) → falls back to a plain GitHub API tag
///     comparison so the banner still works, but self-update is unavailable.
///
/// Results are cached to stay well under GitHub's unauthenticated rate limit.
/// </summary>
public class UpdateService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<UpdateService> _log;
    private readonly string _repo;       // "owner/repo"
    private readonly string _repoUrl;    // "https://github.com/owner/repo"
    private readonly string _current;
    private readonly bool _enabled;

    private readonly UpdateManager? _mgr;
    private readonly bool _isInstalled;

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _cache;
    private DateTime _cacheUntil = DateTime.MinValue;
    private Velopack.UpdateInfo? _pending;   // the Velopack update to apply, if any

    public UpdateService(IConfiguration config, IHttpClientFactory http, ILogger<UpdateService> log)
    {
        _http = http;
        _log = log;
        _repo = config["UpdateCheck:GitHubRepo"] ?? "";
        _enabled = (config.GetValue<bool?>("UpdateCheck:Enabled") ?? true) && !string.IsNullOrWhiteSpace(_repo);
        _repoUrl = string.IsNullOrWhiteSpace(_repo) ? "" : $"https://github.com/{_repo}";
        var prerelease = config.GetValue<bool?>("UpdateCheck:Prerelease") ?? false;

        if (_enabled)
        {
            try
            {
                _mgr = new UpdateManager(new GithubSource(_repoUrl, null, prerelease));
                _isInstalled = _mgr.IsInstalled;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Velopack UpdateManager could not be initialised; self-update disabled");
                _mgr = null;
                _isInstalled = false;
            }
        }

        // Version priority: explicit override → installed (Velopack) version → assembly version.
        var configured = config["UpdateCheck:CurrentVersion"];
        _current = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : (_isInstalled ? _mgr?.CurrentVersion?.ToString() : null)
                ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
                ?? "1.0.0";
    }

    public async Task<UpdateInfo> GetAsync(bool force, CancellationToken ct = default)
    {
        if (!_enabled)
            return new UpdateInfo
            {
                Enabled = false, UpdateAvailable = false, CanSelfUpdate = false, CurrentVersion = _current,
                Message = "Update checks are off. Set UpdateCheck:GitHubRepo to \"owner/repo\"."
            };

        if (!force && _cache != null && DateTime.UtcNow < _cacheUntil) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _cache != null && DateTime.UtcNow < _cacheUntil) return _cache;
            _cache = _isInstalled ? await CheckViaVelopackAsync() : await CheckViaGitHubApiAsync(ct);
            _cacheUntil = DateTime.UtcNow.Add(Ttl);
            return _cache;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Downloads the pending update and restarts the app onto the new version.
    /// Only valid when the app was installed via the Velopack installer.
    /// The restart is deferred briefly so the HTTP response can flush first.
    /// Returns the version being applied.
    /// </summary>
    public async Task<string> DownloadAndRestartAsync()
    {
        if (!_isInstalled || _mgr == null)
            throw new InvalidOperationException(
                "Self-update is only available when the app was installed via the installer. Download the release from GitHub instead.");

        var upd = _pending ?? await _mgr.CheckForUpdatesAsync();
        if (upd == null)
            throw new InvalidOperationException("No update is available to apply.");

        await _mgr.DownloadUpdatesAsync(upd);
        var version = upd.TargetFullRelease.Version.ToString();

        // Defer the restart so this request's response reaches the browser first.
        _ = Task.Run(async () =>
        {
            await Task.Delay(750);
            try { _mgr.ApplyUpdatesAndRestart(upd); }
            catch (Exception ex) { _log.LogError(ex, "Failed to apply update / restart"); }
        });

        return version;
    }

    private async Task<UpdateInfo> CheckViaVelopackAsync()
    {
        var info = new UpdateInfo
        {
            Enabled = true, CurrentVersion = _current, CanSelfUpdate = true,
            Url = $"{_repoUrl}/releases/latest"
        };
        try
        {
            var upd = await _mgr!.CheckForUpdatesAsync();
            _pending = upd;
            if (upd == null)
            {
                info.Message = "You're up to date.";
            }
            else
            {
                info.UpdateAvailable = true;
                info.LatestVersion = upd.TargetFullRelease.Version.ToString();
                info.Message = $"Version {info.LatestVersion} is available.";
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Velopack update check failed");
            info.Message = "Update check failed: " + ex.Message;
        }
        return info;
    }

    private async Task<UpdateInfo> CheckViaGitHubApiAsync(CancellationToken ct)
    {
        // Not a Velopack install (dev / portable). Show the banner via a plain tag
        // comparison, but self-update is unavailable — link to the release instead.
        var info = new UpdateInfo { Enabled = true, CurrentVersion = _current, CanSelfUpdate = false };
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
