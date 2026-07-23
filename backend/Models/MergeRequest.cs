namespace BranchMerger.Api.Models;

public class MergeRequest
{
    public string SourceBranch { get; set; } = "";   // branch merged FROM (e.g. master)
    public string TargetBranch { get; set; } = "";   // branch merged INTO (e.g. feature/x)
    public bool Push { get; set; } = true;            // push target back to remote after merging
}
