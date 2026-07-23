using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UpdateController : ControllerBase
{
    private readonly UpdateService _svc;
    public UpdateController(UpdateService svc) => _svc = svc;

    /// <summary>Cached update status (checked against GitHub Releases).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _svc.GetAsync(false, ct));

    /// <summary>Force a fresh check now.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(CancellationToken ct) => Ok(await _svc.GetAsync(true, ct));

    /// <summary>
    /// Download the available update and restart onto it (Velopack installs only).
    /// Returns immediately; the app restarts shortly after the response is sent.
    /// </summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply()
    {
        try
        {
            var version = await _svc.DownloadAndRestartAsync();
            return Ok(new { restarting = true, version });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
