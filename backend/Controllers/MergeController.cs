using System.Text;
using System.Text.Json;
using BranchMerger.Api.Models;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MergeController : ControllerBase
{
    private readonly IGitService _git;
    private readonly NotificationService _notifier;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public MergeController(IGitService git, NotificationService notifier)
    {
        _git = git;
        _notifier = notifier;
    }

    /// <summary>Runs a merge immediately (the "Merge now" button).</summary>
    [HttpPost]
    public async Task<ActionResult<MergeResult>> Merge([FromBody] MergeRequest req, CancellationToken ct)
    {
        var result = await _git.MergeAsync(req.SourceBranch, req.TargetBranch, req.Push, ct: ct);

        // Even though the UI shows this result directly, still fire notifications so
        // configured channels (Slack/email) alert the team about a conflict.
        if (result.IsConflict)
            await _notifier.NotifyConflictAsync(result, req.SourceBranch, req.TargetBranch, "Manual merge", ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Runs a merge and streams each git step live (Server-Sent Events) so the UI can
    /// show the process as it happens. Emits "step" events, then a final "result" event
    /// carrying the full MergeResult. Nothing is persisted server-side.
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] MergeRequest req, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";   // don't let a proxy buffer the stream
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        async Task Send(string ev, string data)
        {
            var sb = new StringBuilder();
            sb.Append("event: ").Append(ev).Append('\n');
            foreach (var line in data.Replace("\r", "").Split('\n'))
                sb.Append("data: ").Append(line).Append('\n');
            sb.Append('\n');
            await Response.WriteAsync(sb.ToString(), ct);
            await Response.Body.FlushAsync(ct);
        }

        var result = await _git.MergeAsync(req.SourceBranch, req.TargetBranch, req.Push,
            onStep: line => Send("step", line), ct: ct);

        if (result.IsConflict)
            await _notifier.NotifyConflictAsync(result, req.SourceBranch, req.TargetBranch, "Manual merge", ct);

        await Send("result", JsonSerializer.Serialize(result, _json));
    }
}
