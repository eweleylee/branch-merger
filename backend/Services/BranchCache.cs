using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

/// <summary>
/// Thread-safe singleton holding the most recently fetched branch list.
/// The background fetcher writes to it; controllers read from it so the UI
/// never has to wait on a live git call.
/// </summary>
public class BranchCache
{
    private readonly object _lock = new();
    private IReadOnlyList<BranchInfo> _branches = Array.Empty<BranchInfo>();

    public DateTime? LastUpdatedUtc { get; private set; }
    public string? LastError { get; private set; }

    public void Set(IReadOnlyList<BranchInfo> branches)
    {
        lock (_lock)
        {
            _branches = branches;
            LastUpdatedUtc = DateTime.UtcNow;
            LastError = null;
        }
    }

    public void SetError(string error)
    {
        lock (_lock) { LastError = error; }
    }

    public IReadOnlyList<BranchInfo> Get()
    {
        lock (_lock) { return _branches; }
    }
}
