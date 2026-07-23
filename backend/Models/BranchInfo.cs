namespace BranchMerger.Api.Models;

public class BranchInfo
{
    public string Name { get; set; } = "";        // e.g. "origin/feature/login" or "main"
    public string ShortName { get; set; } = "";    // e.g. "feature/login" (remote prefix stripped)
    public bool IsRemote { get; set; }
    public string Commit { get; set; } = "";       // short sha
    public string LastCommitDate { get; set; } = "";
}
