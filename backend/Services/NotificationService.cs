using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>
/// Fans a notification out to every enabled channel. A failure in one channel
/// (e.g. a dead webhook) never blocks the others or the merge that triggered it.
/// </summary>
public class NotificationService
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(IEnumerable<INotificationChannel> channels, ILogger<NotificationService> log)
    {
        _channels = channels;
        _log = log;
    }

    public async Task NotifyAsync(Notification n, CancellationToken ct = default)
    {
        foreach (var ch in _channels.Where(c => c.Enabled))
        {
            try { await ch.SendAsync(n, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Channel {Channel} failed", ch.GetType().Name); }
        }
    }

    /// <summary>Convenience builder for a merge-conflict alert from a MergeResult.</summary>
    public Task NotifyConflictAsync(MergeResult result, string source, string target, string trigger, CancellationToken ct = default)
        => NotifyAsync(new Notification
        {
            Level = NotificationLevel.Warning,
            Title = "Merge conflict",
            Message = result.Message,
            Trigger = trigger,
            SourceBranch = source,
            TargetBranch = target,
            ConflictedFiles = result.ConflictedFiles
        }, ct);

    /// <summary>Alert for a scheduled merge that failed for a non-conflict reason.</summary>
    public Task NotifyFailureAsync(MergeResult result, string source, string target, string trigger, CancellationToken ct = default)
        => NotifyAsync(new Notification
        {
            Level = NotificationLevel.Error,
            Title = "Merge failed",
            Message = result.Message,
            Trigger = trigger,
            SourceBranch = source,
            TargetBranch = target
        }, ct);
}
