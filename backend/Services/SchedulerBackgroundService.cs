using BranchMerger.Api.Models;
using Cronos;

namespace BranchMerger.Api.Services;

/// <summary>
/// Wakes up every 15 seconds and runs any schedule whose next-run time has passed.
/// One-time schedules disable themselves after running; cron schedules compute
/// their next occurrence. Only one merge runs at a time (GitService serialises them).
/// </summary>
public class SchedulerBackgroundService : BackgroundService
{
    private readonly ScheduleStore _store;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SchedulerBackgroundService> _log;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    public SchedulerBackgroundService(
        ScheduleStore store,
        IServiceProvider sp,
        ILogger<SchedulerBackgroundService> log)
    {
        _store = store;
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Make sure every schedule has a NextRunUtc computed on startup.
        foreach (var s in _store.GetAll())
        {
            if (s.Enabled && s.NextRunUtc == null)
            {
                s.NextRunUtc = ComputeNextRun(s, DateTime.UtcNow);
                _store.Upsert(s);
            }
        }

        _log.LogInformation("Scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueSchedules(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed"); }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunDueSchedules(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Run everything that's due, ordered by run time first, then by the
        // user-defined Order so that same-time schedules run in the chosen sequence.
        var due = _store.GetAll()
            .Where(s => s.Enabled && s.NextRunUtc != null && s.NextRunUtc <= now)
            .OrderBy(s => s.NextRunUtc)
            .ThenBy(s => s.Order)
            .ToList();

        foreach (var s in due)
        {
            _log.LogInformation("Running schedule {Id}: {Src} -> {Tgt}", s.Id, s.SourceBranch, s.TargetBranch);

            var git = _sp.GetRequiredService<IGitService>();
            var result = await git.MergeAsync(s.SourceBranch, s.TargetBranch, s.Push, ct);

            s.LastRunUtc = DateTime.UtcNow;
            s.LastStatus = result.Success ? "Success" : "Failed: " + result.Message;

            // Nobody is watching a scheduled run, so alert on any problem.
            if (!result.Success)
            {
                var notifier = _sp.GetRequiredService<NotificationService>();
                if (result.IsConflict)
                    await notifier.NotifyConflictAsync(result, s.SourceBranch, s.TargetBranch, "Scheduled merge", ct);
                else
                    await notifier.NotifyFailureAsync(result, s.SourceBranch, s.TargetBranch, "Scheduled merge", ct);
            }

            if (s.Type == ScheduleType.Once)
            {
                s.Enabled = false;      // one-shot is done
                s.NextRunUtc = null;
            }
            else
            {
                s.NextRunUtc = ComputeNextRun(s, DateTime.UtcNow);
            }

            _store.Upsert(s);
        }
    }

    public static DateTime? ComputeNextRun(MergeSchedule s, DateTime fromUtc)
    {
        if (s.Type == ScheduleType.Once)
            return s.RunAtUtc;

        if (string.IsNullOrWhiteSpace(s.CronExpression))
            return null;

        var cron = CronExpression.Parse(s.CronExpression);
        // Interpret the expression in the server PC's local timezone (Cronos still
        // returns a UTC instant, so NextRunUtc / comparisons stay correct). DST, if
        // the local zone has any, is handled by Cronos.
        return cron.GetNextOccurrence(fromUtc, TimeZoneInfo.Local);
    }
}
