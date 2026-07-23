using System.Text.Json.Serialization;

namespace BranchMerger.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleType
{
    Once,   // runs a single time at RunAtUtc
    Cron    // recurring, driven by CronExpression
}

public class MergeSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public bool Push { get; set; } = true;

    public ScheduleType Type { get; set; } = ScheduleType.Once;
    public DateTime? RunAtUtc { get; set; }            // used when Type == Once
    public string? CronExpression { get; set; }        // used when Type == Cron (UTC)

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Execution order among schedules that fire at the SAME time (lower runs first).
    /// Set by drag-to-reorder in the UI. Irrelevant across different times — the
    /// scheduler always orders by run time first, then by this within a tie.
    /// </summary>
    public int Order { get; set; }

    // runtime state
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public string? LastStatus { get; set; }            // "Success" / "Failed: ..."
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Payload the frontend posts to create a schedule.</summary>
public class CreateScheduleDto
{
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public bool Push { get; set; } = true;
    public ScheduleType Type { get; set; } = ScheduleType.Once;
    public DateTime? RunAtUtc { get; set; }
    public string? CronExpression { get; set; }
}
