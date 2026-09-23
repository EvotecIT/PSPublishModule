using PowerForge;
using System.Diagnostics;
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
        _repositories = repositories ?? new GitWorkspaceRepositorySource();
    }

    public Task<WorkspaceStorageSnapshot> InspectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
        => InspectAsync(workspaceRoot, null, cancellationToken);

    public async Task<WorkspaceStorageSnapshot> InspectAsync(
        string workspaceRoot,
        IProgress<WorkspaceStorageScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var repositories = await _repositories.DiscoverAsync(root, cancellationToken).ConfigureAwait(false);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inspected = new HashSet<string>(comparer);
        var entries = new List<WorkspaceStorageEntry>();
        var incompleteRegistrations = 0;

        var orderedRepositories = repositories.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var repositoryIndex = 0; repositoryIndex < orderedRepositories.Length; repositoryIndex++)
        {
            var repository = orderedRepositories[repositoryIndex];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(repositoryIndex, orderedRepositories.Length, repository.RootPath, 0, 0));
            var listing = await ReadWorktreesAsync(repository.RootPath, cancellationToken).ConfigureAwait(false);
            if (!listing.Complete)
                incompleteRegistrations++;
            var registered = listing.Items;
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
                    repositoryIndex,
                    orderedRepositories.Length,
                    progress,
                    cancellationToken).ConfigureAwait(false));
            }
            progress?.Report(new(repositoryIndex + 1, orderedRepositories.Length, repository.RootPath, 0, 0));
        }

        var ordered = entries
            .OrderBy(static item => item.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static item => item.IsPrimary)
            .ThenBy(static item => item.Branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var registrationWarning = incompleteRegistrations == 0 ? null
            : $"Git worktree registrations could not be read completely for {incompleteRegistrations} repositories. Working-copy counts and sizes are partial.";
        (IReadOnlyList<WorkspaceStorageOtherFolder> otherFolders, string? otherFolderWarning) = incompleteRegistrations == 0
            ? InspectOtherFolders(root, inspected, orderedRepositories.Length, progress, cancellationToken)
            : ([], "Other _worktrees folders were not classified because Git registrations are incomplete.");
        cancellationToken.ThrowIfCancellationRequested();
        return new WorkspaceStorageSnapshot(
            root,
            DateTimeOffset.UtcNow,
            ordered.Sum(static item => item.SizeBytes),
            ordered.Where(static item => !item.IsPrimary).Sum(static item => item.SizeBytes),
            ordered.Count(static item => item.IsReviewCandidate),
            ordered,
            otherFolders,
            otherFolderWarning,
            registrationWarning);
    }

    private static (IReadOnlyList<WorkspaceStorageOtherFolder> Folders, string? Warning) InspectOtherFolders(
        string workspaceRoot,
        HashSet<string> registeredPaths,
        int repositoryCount,
        IProgress<WorkspaceStorageScanProgress>? progress,
        CancellationToken token)
    {
        if (WorktreeDetector.IsGitRepository(workspaceRoot)) return ([], null);
        var container = Path.Combine(workspaceRoot, "_worktrees");
        if (!Directory.Exists(container)) return ([], null);
        string[] paths;
        try
        {
            if ((File.GetAttributes(container) & FileAttributes.ReparsePoint) != 0)
                return ([], "The _worktrees container is linked and was not inspected.");
            paths = Directory.GetDirectories(container);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ([], "The _worktrees container could not be read; registered working-copy evidence remains available.");
        }

        var folders = new List<WorkspaceStorageOtherFolder>();
        foreach (var path in paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (registeredPaths.Contains(fullPath)) continue;

            try
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    folders.Add(new(fullPath, "Linked directory", 0, 0, "Linked folder was not measured or followed.", Measured: false));
                    continue;
                }

                var gitPath = Path.Combine(fullPath, ".git");
                var gitLink = File.Exists(gitPath) ? WorktreeDetector.ResolveGitAdministrativeDirectory(fullPath) : null;
                var kind = Directory.Exists(gitPath) ? "Independent Git checkout"
                    : gitLink is not null ? "Git-linked folder"
                    : File.Exists(gitPath) ? "Folder with .git file"
                    : "Folder without Git metadata";
                var gitMetadataState = gitLink is null
                    ? File.Exists(gitPath) ? "Git link could not be resolved" : null
                    : Directory.Exists(gitLink) ? "Git administrative target present" : "Git administrative target missing";
                progress?.Report(new(repositoryCount, repositoryCount, fullPath, 0, 0, IsOtherFolder: true));
                var measured = MeasureDirectory(fullPath, token, progress, repositoryCount, repositoryCount, isOtherFolder: true);
                folders.Add(new(fullPath, kind, measured.Bytes, measured.Items, measured.Warning,
                    GitMetadataState: gitMetadataState));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                folders.Add(new(fullPath, "Unavailable folder", 0, 0, "This folder could not be inspected.", Measured: false));
            }
        }
        return (folders, null);
    }

    private async Task<WorkspaceStorageEntry> InspectWorkingCopyAsync(
        string repositoryName,
        string path,
        string primaryPath,
        RegisteredWorktree registered,
        string? defaultBranch,
        int completedRepositories,
        int totalRepositories,
        IProgress<WorkspaceStorageScanProgress>? progress,
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

            var measured = MeasureDirectory(path, token, progress, completedRepositories, totalRepositories);
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

    private async Task<RegistrationListing> ReadWorktreesAsync(string repositoryRoot, CancellationToken token)
    {
        var result = await _git.RunRawAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken: token).ConfigureAwait(false);
        if (!result.Succeeded)
            return new(false, []);
        var entries = new List<RegisteredWorktree>();
        var complete = true;
        string? path = null;
        string? branch = null;
        var locked = false;
        var bare = false;
        string? prunable = null;

        void Flush()
        {
            if (path is not null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path)) complete = false;
                    else entries.Add(new RegisteredWorktree(Path.GetFullPath(path), branch, locked, bare, prunable));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    complete = false;
                }
            }
            path = null; branch = null; locked = false; bare = false; prunable = null;
        }

        foreach (var line in result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line[9..];
            }
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal) && path is not null) branch = line[18..];
            else if (line == "detached" && path is not null) branch = "(detached)";
            else if (line.StartsWith("locked", StringComparison.Ordinal) && path is not null) locked = true;
            else if (line == "bare" && path is not null) bare = true;
            else if (line.StartsWith("prunable", StringComparison.Ordinal) && path is not null)
                prunable = line.Length > 9 ? line[9..].Trim() : "Git marked this worktree prunable.";
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal) && path is not null) { }
            else complete = false;
        }
        Flush();
        return new(complete && entries.Count > 0, entries);
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

    private static DirectoryMeasurement MeasureDirectory(string root, CancellationToken token,
        IProgress<WorkspaceStorageScanProgress>? progress, int completedRepositories, int totalRepositories,
        bool isOtherFolder = false)
    {
        long bytes = 0;
        var items = 1;
        string? warning = null;
        var pending = new Stack<string>();
        pending.Push(root);
        var lastReport = Stopwatch.GetTimestamp();
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
                    if (progress is not null && Stopwatch.GetElapsedTime(lastReport) >= TimeSpan.FromSeconds(1))
                    {
                        progress.Report(new(completedRepositories, totalRepositories, root, bytes, items, isOtherFolder));
                        lastReport = Stopwatch.GetTimestamp();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning ??= "Some entries could not be measured.";
            }
        }
        progress?.Report(new(completedRepositories, totalRepositories, root, bytes, items, isOtherFolder));
        return new DirectoryMeasurement(bytes, items, warning);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? Sanitize(string text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim().Split('\n', '\r')[0];

    private sealed record RegisteredWorktree(string Path, string? Branch, bool IsLocked, bool IsBare, string? PrunableReason);
    private sealed record RegistrationListing(bool Complete, IReadOnlyList<RegisteredWorktree> Items);
    private sealed record DirectoryMeasurement(long Bytes, int Items, string? Warning);
}
