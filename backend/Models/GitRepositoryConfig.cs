namespace BranchMerger.Api.Models;

/// <summary>
/// Git configuration. Edited at runtime via the Settings screen (persisted to
/// settings.json), seeded on first run from appsettings.json.
/// RepositoryPath should be a dedicated working clone the app is allowed to
/// checkout/reset. If it doesn't exist yet, set RepositoryUrl and use "Clone".
/// </summary>
public class GitRepositoryConfig
{
    public string RepositoryPath { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string RemoteName { get; set; } = "origin";
    public int FetchIntervalSeconds { get; set; } = 60;
}
