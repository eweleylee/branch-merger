using BranchMerger.Api.Models;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppSettingsStore _settings;
    private readonly IGitService _git;
    private readonly BranchCache _cache;

    public SettingsController(AppSettingsStore settings, IGitService git, BranchCache cache)
    {
        _settings = settings;
        _git = git;
        _cache = cache;
    }

    /// <summary>Current settings, with the email password masked.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        settings = _settings.ForDisplay()
    });

    /// <summary>Save settings. A blank email password means "keep the existing one".</summary>
    [HttpPut]
    public IActionResult Update([FromBody] AppSettings incoming)
    {
        if (incoming == null) return BadRequest(new { message = "No settings provided." });
        _settings.Update(incoming);
        return Ok(new { settings = _settings.ForDisplay() });
    }

    /// <summary>Is the configured working clone ready to use?</summary>
    [HttpGet("repo-status")]
    public async Task<IActionResult> RepoStatus(CancellationToken ct) =>
        Ok(await _git.GetRepoStatusAsync(ct));

    /// <summary>Clone from the configured URL (or confirm an existing clone), then refresh branches.</summary>
    [HttpPost("clone")]
    public async Task<IActionResult> Clone(CancellationToken ct)
    {
        var status = await _git.EnsureRepositoryAsync(ct);
        if (status.Ready)
            _cache.Set(await _git.GetBranchesAsync(ct));
        return status.Ready ? Ok(status) : BadRequest(status);
    }
}
