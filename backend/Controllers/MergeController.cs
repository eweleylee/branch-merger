using BranchMerger.Api.Models;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MergeController : ControllerBase
{
    private readonly IGitService _git;
    private readonly NotificationService _notifier;

    public MergeController(IGitService git, NotificationService notifier)
    {
        _git = git;
        _notifier = notifier;
    }

    /// <summary>Runs a merge immediately (the "Merge now" button).</summary>
    [HttpPost]
    public async Task<ActionResult<MergeResult>> Merge([FromBody] MergeRequest req, CancellationToken ct)
    {
        var result = await _git.MergeAsync(req.SourceBranch, req.TargetBranch, req.Push, ct);

        // Even though the UI shows this result directly, still fire notifications so
        // configured channels (Slack/email) alert the team about a conflict.
        if (result.IsConflict)
            await _notifier.NotifyConflictAsync(result, req.SourceBranch, req.TargetBranch, "Manual merge", ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }
}
