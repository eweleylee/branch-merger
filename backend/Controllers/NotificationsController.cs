using BranchMerger.Api.Models;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationStore _store;
    private readonly NotificationService _notifier;

    public NotificationsController(NotificationStore store, NotificationService notifier)
    {
        _store = store;
        _notifier = notifier;
    }

    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        items = _store.GetAll(),
        unread = _store.UnreadCount()
    });

    [HttpPost("read-all")]
    public IActionResult MarkAllRead() { _store.MarkAllRead(); return NoContent(); }

    [HttpPost("{id:guid}/read")]
    public IActionResult MarkRead(Guid id) { _store.MarkRead(id); return NoContent(); }

    [HttpPost("clear")]
    public IActionResult Clear() { _store.Clear(); return NoContent(); }

    /// <summary>Sends a test notification so you can verify webhook/email wiring.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        await _notifier.NotifyAsync(new Notification
        {
            Level = NotificationLevel.Info,
            Title = "Test notification",
            Message = "If you can see this, notifications are working.",
            Trigger = "Manual test"
        }, ct);
        return NoContent();
    }
}
