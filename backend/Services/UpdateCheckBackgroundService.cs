namespace BranchMerger.Api.Services;

/// <summary>
/// Periodically re-checks GitHub Releases while the app runs, so a long-running
/// instance surfaces the "update available" banner/notification on its own without
/// needing the page reloaded. UpdateService caches + de-dupes, so this only hits
/// GitHub on the interval and only notifies once per new version.
/// </summary>
public class UpdateCheckBackgroundService : BackgroundService
{
    private readonly UpdateService _update;
    private readonly ILogger<UpdateCheckBackgroundService> _log;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    public UpdateCheckBackgroundService(UpdateService update, ILogger<UpdateCheckBackgroundService> log)
    {
        _update = update;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await _update.GetAsync(force: true, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Periodic update check failed"); }

            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
