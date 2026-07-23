namespace BranchMerger.Api.Services;

/// <summary>
/// Resolves where the app keeps its persistent data (settings, schedules,
/// notifications). This lives in a stable per-user directory OUTSIDE the program
/// folder, so replacing/rebuilding the app never touches your data.
///
///   Windows : %APPDATA%\BranchMerger
///   macOS   : ~/.config/BranchMerger
///   Linux   : ~/.config/BranchMerger   (or $XDG_CONFIG_HOME)
///
/// Override with the "DataDirectory" setting if you want a custom location.
/// </summary>
public class AppPaths
{
    public string DataDirectory { get; }
    public string SettingsFile { get; }
    public string SchedulesFile { get; }
    public string NotificationsFile { get; }

    public AppPaths(IConfiguration config, ILogger<AppPaths> log)
    {
        var configured = config["DataDirectory"];
        DataDirectory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BranchMerger")
            : configured;

        Directory.CreateDirectory(DataDirectory);

        SettingsFile = Resolve(config["SettingsFilePath"] ?? "settings.json");
        SchedulesFile = Resolve(config["SchedulesFilePath"] ?? "schedules.json");
        NotificationsFile = Resolve(config["NotificationsFilePath"] ?? "notifications.json");

        MigrateLegacy(new[] { SettingsFile, SchedulesFile, NotificationsFile });

        log.LogInformation("Data directory: {Dir}", DataDirectory);
    }

    private string Resolve(string fileOrPath) =>
        Path.IsPathRooted(fileOrPath) ? fileOrPath : Path.Combine(DataDirectory, fileOrPath);

    /// <summary>
    /// One-time best-effort move: if an older build left data next to the
    /// executable, copy it into the new per-user directory so nothing is lost.
    /// </summary>
    private void MigrateLegacy(IEnumerable<string> targets)
    {
        foreach (var target in targets)
        {
            try
            {
                if (File.Exists(target)) continue; // already have it here
                var legacy = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(target));
                if (File.Exists(legacy)) File.Copy(legacy, target);
            }
            catch { /* migration is best-effort */ }
        }
    }
}
