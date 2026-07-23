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
}
