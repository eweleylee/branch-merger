using BranchMerger.Api.Models;
using BranchMerger.Api.Services;
using Cronos;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchedulesController : ControllerBase
{
    private readonly ScheduleStore _store;
    public SchedulesController(ScheduleStore store) => _store = store;

    [HttpGet]
    public IActionResult GetAll() => Ok(_store.GetAll());

    [HttpPost]
    public IActionResult Create([FromBody] CreateScheduleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SourceBranch) || string.IsNullOrWhiteSpace(dto.TargetBranch))
            return BadRequest(new { message = "Source and target branches are required." });
        if (dto.SourceBranch == dto.TargetBranch)
            return BadRequest(new { message = "Source and target must be different." });

        var schedule = new MergeSchedule
        {
            SourceBranch = dto.SourceBranch,
            TargetBranch = dto.TargetBranch,
            Push = dto.Push,
            Type = dto.Type,
            RunAtUtc = dto.RunAtUtc,
            CronExpression = dto.CronExpression,
            Enabled = true,
            Order = (_store.GetAll().Select(x => x.Order).DefaultIfEmpty(0).Max()) + 10
        };

        if (dto.Type == ScheduleType.Once)
        {
            if (dto.RunAtUtc == null)
                return BadRequest(new { message = "RunAtUtc is required for a one-time schedule." });
            if (dto.RunAtUtc <= DateTime.UtcNow)
                return BadRequest(new { message = "Scheduled time must be in the future." });
        }
        else // Cron
        {
            if (string.IsNullOrWhiteSpace(dto.CronExpression))
                return BadRequest(new { message = "A cron expression is required for a recurring schedule." });
            try { CronExpression.Parse(dto.CronExpression); }
            catch (Exception ex) { return BadRequest(new { message = "Invalid cron expression: " + ex.Message }); }
        }

        schedule.NextRunUtc = SchedulerBackgroundService.ComputeNextRun(schedule, DateTime.UtcNow);
        _store.Upsert(schedule);
        return Ok(schedule);
    }

    /// <summary>
    /// Persist a new execution order. The body is the full list of schedule IDs in
    /// the order shown in the UI; Order is written from position. Only affects
    /// same-time schedules at run time, but storing a single global order is simplest.
    /// </summary>
    [HttpPut("reorder")]
    public IActionResult Reorder([FromBody] List<Guid> orderedIds)
    {
        if (orderedIds == null) return BadRequest(new { message = "No order provided." });

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var s = _store.Get(orderedIds[i]);
            if (s == null) continue;
            s.Order = i;
            _store.Upsert(s);
        }
        return Ok(_store.GetAll());
    }

    [HttpPost("{id:guid}/toggle")]
    public IActionResult Toggle(Guid id)
    {
        var s = _store.Get(id);
        if (s == null) return NotFound();

        s.Enabled = !s.Enabled;
        if (s.Enabled)
            s.NextRunUtc = SchedulerBackgroundService.ComputeNextRun(s, DateTime.UtcNow);
        else
            s.NextRunUtc = null;

        _store.Upsert(s);
        return Ok(s);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) =>
        _store.Remove(id) ? NoContent() : NotFound();
}
