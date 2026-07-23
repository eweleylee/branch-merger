using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly BranchCache _cache;
    private readonly IGitService _git;

    public BranchesController(BranchCache cache, IGitService git)
    {
        _cache = cache;
        _git = git;
    }

    /// <summary>Returns the branches from the background-maintained cache (fast).</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        branches = _cache.Get(),
        lastUpdatedUtc = _cache.LastUpdatedUtc,
        lastError = _cache.LastError
    });

    /// <summary>Forces an immediate fetch + refresh (used by the "Refresh" button).</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        await _git.FetchAsync(ct);
        var branches = await _git.GetBranchesAsync(ct);
        _cache.Set(branches);
        return Ok(new { branches, lastUpdatedUtc = _cache.LastUpdatedUtc });
    }
}
