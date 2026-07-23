using Microsoft.Win32;

namespace BranchMerger.Api.Services;

/// <summary>
/// Registers/removes a per-user "run on Windows login" entry (HKCU\...\Run) that points
/// at the currently-running executable. Called on every startup, so it self-heals across
/// Velopack updates — after an update the new version relaunches and re-points the entry.
///
/// Only acts on Windows AND only for an installed build (a Velopack layout, detected by an
/// Update.exe sitting one directory above the app). Dev runs and non-Windows are no-ops.
/// </summary>
public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BranchMerger";

    public static void Apply(bool enabled, ILogger log)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!IsInstalled())
        {
            log.LogDebug("Not an installed build; skipping Windows startup registration.");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                // "--startup" tells the app it was launched at login, so it runs quietly
                // (no browser pop-up). Manual launches from the shortcut still open the UI.
                var desired = $"\"{exe}\" --startup";
                if ((key.GetValue(ValueName) as string) != desired)
                {
                    key.SetValue(ValueName, desired);
                    log.LogInformation("Registered Windows startup entry -> {Exe}", exe);
                }
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                log.LogInformation("Removed Windows startup entry.");
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not update Windows startup entry");
        }
    }

    /// <summary>
    /// True when running from a Velopack install (Update.exe lives one level above the app
    /// content dir). This path is stable across updates, so the check keeps working.
    /// </summary>
    private static bool IsInstalled()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Directory.GetParent(baseDir)?.FullName;
            return parent != null && File.Exists(Path.Combine(parent, "Update.exe"));
        }
        catch { return false; }
    }
}
