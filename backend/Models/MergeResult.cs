namespace BranchMerger.Api.Models;

public class MergeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Log { get; set; } = "";
    public bool IsConflict { get; set; }
    public List<string> ConflictedFiles { get; set; } = new();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
