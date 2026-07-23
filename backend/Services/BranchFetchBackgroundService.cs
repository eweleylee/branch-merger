namespace BranchMerger.Api.Services;

/// <summary>
/// Continuously fetches from the remote and refreshes the branch cache. The
/// interval is read live from settings each cycle, so changing it in the UI
/// applies without a restart.
/// </summary>
public class BranchFetchBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly BranchCache _cache;
    private readonly AppSettingsStore _settings;
    private readonly ILogger<BranchFetchBackgroundService> _log;

    public BranchFetchBackgroundService(
        IServiceProvider sp,
        BranchCache cache,
        AppSettingsStore settings,
        ILogger<BranchFetchBackgroundService> log)
    {
        _sp = sp;
        _cache = cache;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Branch fetcher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var git = _sp.GetRequiredService<IGitService>();
                await git.FetchAsync(stoppingToken);
                var branches = await git.GetBranchesAsync(stoppingToken);
                _cache.Set(branches);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Branch fetch cycle failed");
                _cache.SetError(ex.Message);
            }

            var seconds = Math.Max(10, _settings.Current.Git.FetchIntervalSeconds);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
