namespace BranchMerger.Api.Services;

/// <summary>
/// Silently prunes log files older than the retention window (default 30 days). Runs
/// once on startup, then every 12 hours, so the logs folder never grows without bound.
/// </summary>
public class LogMaintenanceBackgroundService : BackgroundService
{
    private readonly string _logDir;
    private readonly int _retentionDays;

    public LogMaintenanceBackgroundService(AppPaths paths, IConfiguration config)
    {
        _logDir = paths.LogDirectory;
        _retentionDays = config.GetValue<int?>("FileLog:RetentionDays") ?? 30;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            LogCleanup.Run(_logDir, _retentionDays);
            try { await Task.Delay(TimeSpan.FromHours(12), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
