using System.Diagnostics;
using System.Net.Sockets;
using BranchMerger.Api.Services;
using Velopack;

// MUST run first. Handles Velopack install/update/uninstall hooks (special CLI
// args) and exits early for those; a harmless no-op under `dotnet run` / portable
// builds. Everything else only runs for a normal launch. On uninstall, offer to
// remove the user's data (kept by default).
var velopackApp = VelopackApp.Build();
if (OperatingSystem.IsWindows())
    velopackApp = velopackApp.OnBeforeUninstallFastCallback(_ => UninstallHook.OnBeforeUninstall());
velopackApp.Run();

var builder = WebApplication.CreateBuilder(args);

// --- Single instance ---
// If an instance is already serving the app URL, don't start a second one. A manual /
// Start-Menu launch just opens the browser to the running instance; an autostart (--startup)
// launch simply exits. Only the first instance actually starts the server.
var appUrl = (builder.Configuration["Urls"] ?? "http://localhost:5080").Split(';')[0];
var launchedAtStartup = args.Contains("--startup");
if (IsAlreadyRunning(appUrl))
{
    if (!launchedAtStartup) TryOpenBrowser(appUrl);
    return;
}

// --- File logging → daily files in the data dir (level from FileLog:MinimumLevel) ---
// Errors incl. merge conflicts (logged at Error) are captured. Useful now Release builds
// are windowless. Old files pruned by a background worker.
if (builder.Configuration.GetValue("FileLog:Enabled", true))
{
    var logDir = Path.Combine(AppPaths.ResolveDataDirectory(builder.Configuration), "logs");
    var minLevel = builder.Configuration.GetValue("FileLog:MinimumLevel", LogLevel.Error);
    builder.Logging.AddProvider(new FileLoggerProvider(logDir, minLevel));
}

// --- Runtime settings (persisted to a stable per-user data dir) ---
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddSingleton<AppSettingsStore>();

// --- Core services (singletons hold shared state) ---
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<BranchCache>();
builder.Services.AddSingleton<ScheduleStore>();

// --- Update check (GitHub Releases) ---
builder.Services.AddHttpClient();
builder.Services.AddSingleton<UpdateService>();

// --- Notifications (in-app feed only) ---
builder.Services.AddSingleton<NotificationStore>();
builder.Services.AddSingleton<INotificationChannel, InAppChannel>();
builder.Services.AddSingleton<NotificationService>();

// --- Background workers ---
builder.Services.AddHostedService<BranchFetchBackgroundService>();   // constantly fetch branches
builder.Services.AddHostedService<SchedulerBackgroundService>();     // run scheduled merges
builder.Services.AddHostedService<UpdateCheckBackgroundService>();   // hourly update check
builder.Services.AddHostedService<LogMaintenanceBackgroundService>(); // prune logs > retention

// --- Web ---
builder.Services.AddControllers();
builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

// Materialise settings on startup so settings.json is created immediately.
var settingsStore = app.Services.GetRequiredService<AppSettingsStore>();

// Apply the "run on Windows login" preference (no-op in dev / non-installed / non-Windows).
// Re-applied here on every launch so it self-heals across updates.
WindowsStartup.Apply(
    settingsStore.Current.RunOnStartup,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WindowsStartup"));

app.UseCors("dev");

// Serve the built Vue app (single-server production mode). In dev there is no
// wwwroot and the UI is served by Vite on :5173 instead — these are harmless then.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");   // let the SPA handle client-side routes / refreshes

// In the packaged (production) build, print the URL and open the browser on start
// (unless this was an automatic startup launch — the login instance runs quietly).
if (!app.Environment.IsDevelopment() && !launchedAtStartup)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Console.WriteLine();
        Console.WriteLine($"  Branch Merger is running →  {appUrl}");
        Console.WriteLine("  Keep this window open. Close it to stop the app.");
        Console.WriteLine();
        TryOpenBrowser(appUrl);
    });
}

app.Run();

// True if something is already listening on the app URL's host:port (i.e. another instance
// is serving). Connection-refused returns fast; a short timeout bounds the worst case.
static bool IsAlreadyRunning(string url)
{
    try
    {
        var uri = new Uri(url);
        using var client = new TcpClient();
        return client.ConnectAsync(uri.Host, uri.Port).Wait(600) && client.Connected;
    }
    catch { return false; }
}

static void TryOpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }
    catch { /* URL is printed above; the user can open it manually */ }
}
