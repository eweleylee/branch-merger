using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>A place a notification can be delivered. Currently only the in-app feed.</summary>
public interface INotificationChannel
{
    bool Enabled { get; }
    Task SendAsync(Notification n, CancellationToken ct = default);
}

/// <summary>Always-on channel: writes to the in-app feed the UI reads.</summary>
public class InAppChannel : INotificationChannel
{
    private readonly NotificationStore _store;
    public InAppChannel(NotificationStore store) => _store = store;
    public bool Enabled => true;
    public Task SendAsync(Notification n, CancellationToken ct = default)
    {
        _store.Add(n);
        return Task.CompletedTask;
    }
}
