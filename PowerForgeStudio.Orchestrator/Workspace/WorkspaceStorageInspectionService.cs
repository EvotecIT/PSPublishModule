using PowerForge;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Inspects local Git working copies without mutating repositories or contacting remotes.</summary>
public sealed class WorkspaceStorageInspectionService : IWorkspaceStorageInspectionService
{
    private readonly GitClient _git;
    private readonly IWorkspaceRepositorySource _repositories;

    public WorkspaceStorageInspectionService(
        GitClient? git = null,
        IWorkspaceRepositorySource? repositories = null)
    {
        _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromSeconds(20));
        _repositories = repositories ?? new WorkspaceRepositorySource();
    }

    public async Task<WorkspaceStorageSnapshot> InspectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var repositories = await _repositories.DiscoverAsync(root, cancellationToken).ConfigureAwait(false);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inspected = new HashSet<string>(comparer);
        var entries = new List<WorkspaceStorageEntry>();

        foreach (var repository in repositories.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var registered = await ReadWorktreesAsync(repository.RootPath, cancellationToken).ConfigureAwait(false);
            if (registered.Count == 0)
                registered = [new RegisteredWorktree(repository.RootPath, null, false, false, null)];
            var primary = registered.FirstOrDefault(static item => !item.IsBare)?.Path ?? repository.RootPath;
            var defaultBranch = Directory.Exists(primary)
                ? await ResolveDefaultBranchAsync(primary, cancellationToken).ConfigureAwait(false)
                : null;
            var repositoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(primary));

            foreach (var worktree in registered)
            {
                var path = Path.GetFullPath(worktree.Path);
                if (!inspected.Add(path))
                    continue;
                entries.Add(await InspectWorkingCopyAsync(
                    repositoryName,
                    path,
                    Path.GetFullPath(primary),
                    worktree,
                    defaultBranch,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        var ordered = entries
            .OrderBy(static item => item.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static item => item.IsPrimary)
            .ThenBy(static item => item.Branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new WorkspaceStorageSnapshot(
            root,
            DateTimeOffset.UtcNow,
            ordered.Sum(static item => item.SizeBytes),
            ordered.Where(static item => !item.IsPrimary).Sum(static item => item.SizeBytes),
            ordered.Count(static item => item.IsReviewCandidate),
            ordered);
    }

    private async Task<WorkspaceStorageEntry> InspectWorkingCopyAsync(
        string repositoryName,
        string path,
        string primaryPath,
        RegisteredWorktree registered,
        string? defaultBranch,
        CancellationToken token)
    {
        var isPrimary = PathsEqual(path, primaryPath);
        if (!Directory.Exists(path))
            return new(repositoryName, path, primaryPath, registered.Branch ?? "(missing)", isPrimary, false,
                registered.IsLocked, 0, 0, 0, "Broken reference", defaultBranch ?? "", "Not checked",
                "Repair or prune the Git worktree reference", false, registered.PrunableReason);

        try
        {
            var status = await _git.GetStatusAsync(path, token).ConfigureAwait(false);
            if (!status.IsGitRepository || !status.CommandResult.Succeeded)
                return new(repositoryName, path, primaryPath, registered.Branch ?? "(unknown)", isPrimary, true,
                    registered.IsLocked, 0, 0, 0, "Git inspection failed", defaultBranch ?? "", "Not checked",
                    "Inspect Git metadata", false, Sanitize(status.CommandResult.StdErr));

            var measured = MeasureDirectory(path, token);
            var changes = checked(status.TrackedChangeCount + status.UntrackedChangeCount);
            var clean = changes == 0;
            var ancestry = isPrimary || string.IsNullOrWhiteSpace(defaultBranch)
                ? "Not checked"
                : await ReadAncestryAsync(path, defaultBranch, token).ConfigureAwait(false);
            var candidate = !isPrimary && !registered.IsLocked && clean && string.Equals(ancestry, "Locally merged", StringComparison.Ordinal);
            var nextCheck = isPrimary ? "Retain primary checkout"
                : registered.IsLocked ? "Review worktree lock"
                : !clean ? "Preserve or commit local changes"
                : string.Equals(ancestry, "Not merged", StringComparison.Ordinal) ? "Review unmerged commits"
                : candidate ? "Refresh remote / PR and active-use evidence"
                : "Resolve default-branch ancestry";
            return new(repositoryName, path, primaryPath, status.BranchName ?? registered.Branch ?? "(detached)",
                isPrimary, true, registered.IsLocked, measured.Bytes, measured.Items, changes,
                clean ? "Clean" : "Changed", defaultBranch ?? "", ancestry, nextCheck, candidate, measured.Warning);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return new(repositoryName, path, primaryPath, registered.Branch ?? "(unknown)", isPrimary, true,
                registered.IsLocked, 0, 0, 0, "Inspection failed", defaultBranch ?? "", "Not checked",
                "Inspect this working copy", false, ex.Message);
        }
    }

    private async Task<IReadOnlyList<RegisteredWorktree>> ReadWorktreesAsync(string repositoryRoot, CancellationToken token)
    {
        var result = await _git.RunRawAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken: token).ConfigureAwait(false);
        if (!result.Succeeded)
            return [];
        var entries = new List<RegisteredWorktree>();
        string? path = null;
        string? branch = null;
        var locked = false;
        var bare = false;
        string? prunable = null;

        void Flush()
        {
            if (path is not null)
                entries.Add(new RegisteredWorktree(path, branch, locked, bare, prunable));
            path = null; branch = null; locked = false; bare = false; prunable = null;
        }

        foreach (var line in result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line[9..];
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal)) branch = line[18..];
            else if (line == "detached") branch = "(detached)";
            else if (line.StartsWith("locked", StringComparison.Ordinal)) locked = true;
            else if (line == "bare") bare = true;
            else if (line.StartsWith("prunable", StringComparison.Ordinal)) prunable = line.Length > 9 ? line[9..].Trim() : "Git marked this worktree prunable.";
        }
        Flush();
        return entries;
    }

    private async Task<string?> ResolveDefaultBranchAsync(string repositoryRoot, CancellationToken token)
    {
        var symbolic = await _git.RunRawAsync(repositoryRoot,
            ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken: token).ConfigureAwait(false);
        if (symbolic.Succeeded && !string.IsNullOrWhiteSpace(symbolic.StdOut))
            return symbolic.StdOut.Trim();
        foreach (var candidate in new[] { "origin/main", "main", "origin/master", "master" })
        {
            var verify = await _git.RunRawAsync(repositoryRoot,
                ["rev-parse", "--verify", "--quiet", candidate], cancellationToken: token).ConfigureAwait(false);
            if (verify.Succeeded)
                return candidate;
        }
        return null;
    }

    private async Task<string> ReadAncestryAsync(string workingCopy, string defaultBranch, CancellationToken token)
    {
        var result = await _git.RunRawAsync(workingCopy,
            ["merge-base", "--is-ancestor", "HEAD", defaultBranch], cancellationToken: token).ConfigureAwait(false);
        return result.TimedOut ? "Not checked" : result.ExitCode switch
        {
            0 => "Locally merged",
            1 => "Not merged",
            _ => "Not checked"
        };
    }

    private static DirectoryMeasurement MeasureDirectory(string root, CancellationToken token)
    {
        long bytes = 0;
        var items = 1;
        string? warning = null;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(entry);
                    items++;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        warning ??= "Linked entries were not measured.";
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                    else
                        bytes = checked(bytes + new FileInfo(entry).Length);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning ??= "Some entries could not be measured.";
            }
        }
        return new DirectoryMeasurement(bytes, items, warning);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? Sanitize(string text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim().Split('\n', '\r')[0];

    private sealed record RegisteredWorktree(string Path, string? Branch, bool IsLocked, bool IsBare, string? PrunableReason);
    private sealed record DirectoryMeasurement(long Bytes, int Items, string? Warning);
}
