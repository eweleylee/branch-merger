using System.Text;
using System.Text.Json;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly BranchCache _cache;
    private readonly IGitService _git;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

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
        await _git.FetchAsync(ct: ct);
        var branches = await _git.GetBranchesAsync(ct);
        _cache.Set(branches);
        return Ok(new { branches, lastUpdatedUtc = _cache.LastUpdatedUtc });
    }

    /// <summary>
    /// Same as refresh, but streams the git fetch step live (SSE) so the UI can show the
    /// process, then a final "result" event with the refreshed branch list.
    /// </summary>
    [HttpPost("refresh/stream")]
    public async Task RefreshStream(CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
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

        await _git.FetchAsync(onStep: line => Send("step", line), ct: ct);
        var branches = await _git.GetBranchesAsync(ct);
        _cache.Set(branches);
        await Send("result", JsonSerializer.Serialize(new { branches, lastUpdatedUtc = _cache.LastUpdatedUtc }, _json));
    }
}
