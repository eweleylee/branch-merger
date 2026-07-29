using System.Text;
using System.Text.RegularExpressions;
using BranchMerger.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BranchMerger.Api.Controllers;

/// <summary>
/// Exposes the daily log files for the in-app viewer. Read-only; returns entries
/// newest-first. File names are strictly validated so nothing outside the logs dir
/// can be read.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly AppPaths _paths;

    private static readonly Regex NamePattern = new(@"^log-\d{4}-\d{2}-\d{2}\.txt$", RegexOptions.Compiled);
    private static readonly Regex EntryStart =
        new(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[", RegexOptions.Compiled);
    private const int MaxEntries = 3000;   // cap payload; newest kept

    public LogsController(AppPaths paths) => _paths = paths;

    /// <summary>Available daily log files, newest first.</summary>
    [HttpGet]
    public IActionResult List()
    {
        var dir = _paths.LogDirectory;
        if (!Directory.Exists(dir)) return Ok(new { files = Array.Empty<object>() });

        var files = Directory.GetFiles(dir, "log-*.txt")
            .Select(f => new FileInfo(f))
            .Where(fi => NamePattern.IsMatch(fi.Name))
            .OrderByDescending(fi => fi.Name)   // name sorts chronologically
            .Select(fi => new
            {
                name = fi.Name,
                date = Path.GetFileNameWithoutExtension(fi.Name)[4..],
                sizeBytes = fi.Length
            })
            .ToList();

        return Ok(new { files });
    }

    /// <summary>One day's log entries, newest first.</summary>
    [HttpGet("{name}")]
    public IActionResult Get(string name)
    {
        if (!NamePattern.IsMatch(name))
            return BadRequest(new { message = "Invalid log file name." });

        var path = Path.Combine(_paths.LogDirectory, name);
        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "Log file not found." });

        string[] lines;
        try { lines = System.IO.File.ReadAllLines(path); }
        catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }

        // The file is written oldest-first; a new entry begins at a timestamped line,
        // and following (non-timestamped) lines belong to it (e.g. stack traces).
        var parsed = new List<(string Time, string Level, string Text)>();
        StringBuilder? cur = null;
        string curTime = "", curLevel = "Information";

        void Flush()
        {
            if (cur != null) parsed.Add((curTime, curLevel, cur.ToString().TrimEnd()));
        }

        foreach (var line in lines)
        {
            if (EntryStart.IsMatch(line))
            {
                Flush();
                cur = new StringBuilder();
                curTime = line.Length >= 23 ? line[..23] : line;
                var lb = line.IndexOf('[');
                var rb = line.IndexOf(']');
                curLevel = (lb >= 0 && rb > lb) ? line[(lb + 1)..rb] : "Information";
                var msg = (rb >= 0 && rb + 2 <= line.Length) ? line[(rb + 2)..] : "";
                cur.Append(msg);
            }
            else
            {
                cur ??= new StringBuilder();
                cur.Append('\n').Append(line);
            }
        }
        Flush();

        parsed.Reverse();   // newest first
        var total = parsed.Count;
        var entries = parsed
            .Take(MaxEntries)
            .Select(e => new { time = e.Time, level = e.Level, text = e.Text });

        return Ok(new { name, total, truncated = total > MaxEntries, entries });
    }
}
