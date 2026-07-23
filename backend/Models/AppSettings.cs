namespace BranchMerger.Api.Models;

/// <summary>All user-editable settings, stored in settings.json at runtime.</summary>
public class AppSettings
{
    public GitRepositoryConfig Git { get; set; } = new();

    /// <summary>
    /// Start the app automatically when Windows starts (per-user). Only takes effect
    /// for an installed build; ignored in dev / non-Windows.
    /// </summary>
    public bool RunOnStartup { get; set; } = true;
}

/// <summary>Result of checking / preparing the working clone.</summary>
public class RepoStatus
{
    public bool Ready { get; set; }
    public bool JustCloned { get; set; }
    public string Message { get; set; } = "";
    public string? Path { get; set; }
    public string? CurrentBranch { get; set; }
}
