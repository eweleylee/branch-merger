using System.Diagnostics;
using BranchMerger.Api.Services;
using Velopack;

// MUST run first. Handles Velopack install/update/uninstall hooks (special CLI
// args) and exits early for those; a harmless no-op under `dotnet run` / portable
// builds. Everything else only runs for a normal launch.
VelopackApp.Build().Run();

var builder = WebApplication.CreateBuilder(args);

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

// --- Web ---
builder.Services.AddControllers();
builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

// Materialise settings on startup so settings.json is created immediately.
app.Services.GetRequiredService<AppSettingsStore>();

app.UseCors("dev");

// Serve the built Vue app (single-server production mode). In dev there is no
// wwwroot and the UI is served by Vite on :5173 instead — these are harmless then.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");   // let the SPA handle client-side routes / refreshes

// In the packaged (production) build, print the URL and open the browser on start.
if (!app.Environment.IsDevelopment())
{
    var url = (builder.Configuration["Urls"] ?? "http://localhost:5080").Split(';')[0];
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Console.WriteLine();
        Console.WriteLine($"  Branch Merger is running →  {url}");
        Console.WriteLine("  Keep this window open. Close it to stop the app.");
        Console.WriteLine();
        TryOpenBrowser(url);
    });
}

app.Run();

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
