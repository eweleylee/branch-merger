using System.Diagnostics;
using System.Text;
using BranchMerger.Api.Models;

namespace BranchMerger.Api.Services;

public interface IGitService
{
    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default);
    Task FetchAsync(CancellationToken ct = default);
    Task<MergeResult> MergeAsync(string source, string target, bool push, CancellationToken ct = default);
    Task<RepoStatus> GetRepoStatusAsync(CancellationToken ct = default);
    Task<RepoStatus> EnsureRepositoryAsync(CancellationToken ct = default);

    /// <summary>True while a git operation (fetch/merge/clone) is currently running.</summary>
    bool IsBusy { get; }

    /// <summary>
    /// Waits until no git operation is running, then keeps the lock held; dispose the
    /// returned handle to release it. Used to pause all git activity before an in-place
    /// update so a merge/clone/fetch is never interrupted.
    /// </summary>
    Task<IDisposable> AcquireExclusiveAsync(CancellationToken ct = default);
}

/// <summary>
/// Talks to the real `git` CLI via child processes, reusing the machine's git
/// credentials/SSH keys. All settings are read live from AppSettingsStore, so a
/// change in the UI takes effect on the next operation. Mutating operations are
/// serialised behind one lock so the fetcher, a merge and a clone never collide.
/// </summary>
public class GitService : IGitService
{
    private readonly AppSettingsStore _settings;
    private readonly ILogger<GitService> _log;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public GitService(AppSettingsStore settings, ILogger<GitService> log)
    {
        _settings = settings;
        _log = log;
    }

    private GitRepositoryConfig Cfg => _settings.Current.Git;

    // CurrentCount is 1 when the gate is free, 0 while an op holds it.
    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<IDisposable> AcquireExclusiveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() { _gate?.Release(); _gate = null; }
    }

    private record GitRun(int ExitCode, string StdOut, string StdErr)
    {
        public string Combined => (StdOut + "\n" + StdErr).Trim();
    }

    private async Task<GitRun> RunAsync(string args, CancellationToken ct, string? workingDir = null)
    {
        var dir = workingDir ?? Cfg.RepositoryPath;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            throw new InvalidOperationException(
                $"Working directory does not exist: '{dir}'. Configure the repository in Settings.");

        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        return new GitRun(proc.ExitCode, stdOut.ToString().Trim(), stdErr.ToString().Trim());
    }

    /// <summary>Quick check that the configured path exists (deeper validation via GetRepoStatusAsync).</summary>
    private bool RepoPathUsable() =>
        !string.IsNullOrWhiteSpace(Cfg.RepositoryPath) && Directory.Exists(Cfg.RepositoryPath);

    public async Task<RepoStatus> GetRepoStatusAsync(CancellationToken ct = default)
    {
        var git = Cfg;
        if (string.IsNullOrWhiteSpace(git.RepositoryPath))
            return new RepoStatus { Ready = false, Message = "No repository path set. Configure it in Settings." };
        if (!Directory.Exists(git.RepositoryPath))
            return new RepoStatus { Ready = false, Path = git.RepositoryPath,
                Message = "Path does not exist yet. Set a repository URL and click Clone." };

        await _gate.WaitAsync(ct);
        try
        {
            var check = await RunAsync("rev-parse --is-inside-work-tree", ct);
            if (check.ExitCode != 0 || check.StdOut.Trim() != "true")
                return new RepoStatus { Ready = false, Path = git.RepositoryPath,
                    Message = "Path exists but is not a git working tree." };

            var branch = await RunAsync("rev-parse --abbrev-ref HEAD", ct);
            return new RepoStatus
            {
                Ready = true,
                Path = git.RepositoryPath,
                CurrentBranch = branch.ExitCode == 0 ? branch.StdOut.Trim() : null,
                Message = "Repository is ready."
            };
        }
        finally { _gate.Release(); }
    }

    public async Task<RepoStatus> EnsureRepositoryAsync(CancellationToken ct = default)
    {
        var git = Cfg;
        if (string.IsNullOrWhiteSpace(git.RepositoryPath))
            return new RepoStatus { Ready = false, Message = "Set a repository path first." };

        // Already a working tree? Nothing to do.
        if (RepoPathUsable())
        {
            var status = await GetRepoStatusAsync(ct);
            if (status.Ready) return status;
        }

        if (string.IsNullOrWhiteSpace(git.RepositoryUrl))
            return new RepoStatus { Ready = false, Path = git.RepositoryPath,
                Message = "Path is empty/not a repo and no repository URL is set to clone from." };

        await _gate.WaitAsync(ct);
        try
        {
            var parent = Directory.GetParent(git.RepositoryPath.TrimEnd('/', '\\'))?.FullName
                         ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(parent);

            _log.LogInformation("Cloning {Url} into {Path}", git.RepositoryUrl, git.RepositoryPath);
            var clone = await RunAsync($"clone {git.RepositoryUrl} \"{git.RepositoryPath}\"", ct, workingDir: parent);
            if (clone.ExitCode != 0)
                return new RepoStatus { Ready = false, Path = git.RepositoryPath,
                    Message = "Clone failed: " + clone.Combined };

            var branch = await RunAsync("rev-parse --abbrev-ref HEAD", ct);
            return new RepoStatus
            {
                Ready = true, JustCloned = true, Path = git.RepositoryPath,
                CurrentBranch = branch.ExitCode == 0 ? branch.StdOut.Trim() : null,
                Message = "Repository cloned and ready."
            };
        }
        catch (Exception ex)
        {
            return new RepoStatus { Ready = false, Path = git.RepositoryPath, Message = "Clone error: " + ex.Message };
        }
        finally { _gate.Release(); }
    }

    public async Task FetchAsync(CancellationToken ct = default)
    {
        if (!RepoPathUsable()) return;   // nothing to fetch until the repo is set up
        await _gate.WaitAsync(ct);
        try
        {
            var r = await RunAsync($"fetch {Cfg.RemoteName} --prune", ct);
            if (r.ExitCode != 0)
                _log.LogWarning("git fetch failed: {Err}", r.Combined);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        if (!RepoPathUsable()) return Array.Empty<BranchInfo>();
        await _gate.WaitAsync(ct);
        try
        {
            const string fmt = "%(refname:short)|%(objectname:short)|%(committerdate:iso8601)";
            var r = await RunAsync($"for-each-ref --sort=-committerdate --format=\"{fmt}\" refs/heads refs/remotes", ct);
            if (r.ExitCode != 0)
            {
                _log.LogWarning("git for-each-ref failed: {Err}", r.Combined);
                return Array.Empty<BranchInfo>();
            }

            var remote = Cfg.RemoteName;
            var list = new List<BranchInfo>();
            foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;

                var name = parts[0].Trim();
                if (name.EndsWith("/HEAD")) continue;

                var isRemote = name.StartsWith(remote + "/");
                list.Add(new BranchInfo
                {
                    Name = name,
                    ShortName = isRemote ? name[(remote.Length + 1)..] : name,
                    IsRemote = isRemote,
                    Commit = parts[1].Trim(),
                    LastCommitDate = parts.Length > 2 ? parts[2].Trim() : ""
                });
            }

            // Show only remote branches. The app operates on remote refs, and local branches
            // in the dedicated clone are just transient merge byproducts (deleted after each
            // merge) — listing them would duplicate every branch (local + origin/<name>).
            return list.Where(b => b.IsRemote).ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task<MergeResult> MergeAsync(string source, string target, bool push, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            return new MergeResult { Success = false, Message = "Source and target branches are required." };
        if (source == target)
            return new MergeResult { Success = false, Message = "Source and target must be different." };
        if (!RepoPathUsable())
            return new MergeResult { Success = false, Message = "Repository is not configured. Open Settings." };

        await _gate.WaitAsync(ct);
        var log = new StringBuilder();
        try
        {
            var remote = Cfg.RemoteName;

            async Task<GitRun> Step(string args)
            {
                log.AppendLine($"$ git {args}");
                var res = await RunAsync(args, ct);
                if (!string.IsNullOrWhiteSpace(res.Combined)) log.AppendLine(res.Combined);
                log.AppendLine();
                return res;
            }

            // Best-effort: return the clone to the configured resting branch and delete the
            // local target branch we created for the merge, so the clone stays clean and only
            // remote branches remain. Never fails the merge.
            async Task RestAndCleanup(string targetShort)
            {
                var def = Cfg.DefaultBranch?.Trim();
                if (string.IsNullOrWhiteSpace(def)) return;   // empty = stay on target; can't delete current branch
                var defShort = def.StartsWith(remote + "/") ? def[(remote.Length + 1)..] : def;
                await Step($"checkout {defShort}");
                // Delete the local target branch (never the resting branch itself).
                if (!string.Equals(targetShort, defShort, StringComparison.Ordinal))
                    await Step($"branch -D {targetShort}");
            }

            var srcRef = source.StartsWith(remote + "/") ? source : $"{remote}/{source}";
            var tgtShort = target.StartsWith(remote + "/") ? target[(remote.Length + 1)..] : target;
            var tgtRef = $"{remote}/{tgtShort}";

            if ((await Step($"fetch {remote} --prune")).ExitCode != 0)
                return Fail("Fetch failed.", log);

            if ((await Step($"checkout -B {tgtShort} {tgtRef}")).ExitCode != 0)
                return Fail($"Could not check out target branch '{tgtShort}'.", log);

            var merge = await Step($"merge --no-edit {srcRef}");
            if (merge.ExitCode != 0)
            {
                var unmerged = await Step("diff --name-only --diff-filter=U");
                var files = unmerged.StdOut
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim())
                    .Where(f => f.Length > 0)
                    .ToList();

                await Step("merge --abort");
                await RestAndCleanup(tgtShort);

                var result = Fail(
                    files.Count > 0
                        ? $"Merge conflict in {files.Count} file(s). The merge was aborted, nothing was pushed."
                        : "Merge failed. The merge was aborted, nothing was pushed.",
                    log);
                result.IsConflict = files.Count > 0;
                result.ConflictedFiles = files;
                return result;
            }

            if (push)
            {
                if ((await Step($"push {remote} {tgtShort}")).ExitCode != 0)
                    return Fail("Merge succeeded locally but push to remote failed.", log);
            }

            await RestAndCleanup(tgtShort);

            return new MergeResult
            {
                Success = true,
                Message = $"Merged '{source}' into '{target}'" + (push ? " and pushed." : " (local only)."),
                Log = log.ToString().Trim()
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Merge threw");
            return Fail("Unexpected error: " + ex.Message, log);
        }
        finally { _gate.Release(); }
    }

    private static MergeResult Fail(string msg, StringBuilder log) =>
        new() { Success = false, Message = msg, Log = log.ToString().Trim() };
}
