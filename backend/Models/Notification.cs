using System.Text.Json.Serialization;

namespace BranchMerger.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationLevel { Info, Warning, Error }

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public NotificationLevel Level { get; set; } = NotificationLevel.Warning;

    public string Title { get; set; } = "";
    public string Message { get; set; } = "";

    public string? Trigger { get; set; }          // "Manual merge" / "Scheduled merge"
    public string? SourceBranch { get; set; }
    public string? TargetBranch { get; set; }
    public List<string> ConflictedFiles { get; set; } = new();

    public bool Read { get; set; }
}
