using System.Runtime.InteropServices;

namespace BranchMerger.Api.Services;

/// <summary>
/// Runs during Velopack's uninstall (from Windows "Apps &amp; features" or Update.exe
/// --uninstall). Velopack removes the program folder but never touches user data in
/// %APPDATA%\BranchMerger, so this hook offers to delete that data too. Answering "No"
/// (or a silent uninstall) keeps it — and since the data lives outside the install
/// folder, a later reinstall to ANY location picks it up automatically.
/// </summary>
internal static class UninstallHook
{
    private const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_SYSTEMMODAL = 0x1000;
    private const int IDYES = 6;

    public static void OnBeforeUninstall()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BranchMerger");
            if (!Directory.Exists(dataDir)) return;

            // Only ask when there's a desktop to show it on; a silent uninstall keeps data.
            if (!Environment.UserInteractive) return;

            var answer = MessageBoxW(IntPtr.Zero,
                "Do you also want to remove your Branch Merger settings, schedules and notifications?\n\n" +
                "Choose No to keep them — they'll be reused automatically if you reinstall, even to a different folder.",
                "Uninstall Branch Merger",
                MB_YESNO | MB_ICONQUESTION | MB_SYSTEMMODAL);

            if (answer == IDYES)
                Directory.Delete(dataDir, recursive: true);
        }
        catch { /* never block or fail the uninstall */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
