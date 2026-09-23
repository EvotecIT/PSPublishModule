using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Orchestrator.Workspace;

public interface IWorkspaceProjectChangeService
{
    Task<WorkspaceProjectChangeSnapshot> InspectAsync(
        IReadOnlyList<RepositoryCatalogEntry> repositories,
        IProgress<WorkspaceProjectChangeProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads local Git state without measuring files or contacting remotes.</summary>
public sealed class WorkspaceProjectChangeService : IWorkspaceProjectChangeService
{
    internal const int StatusOutputLimit = 64 * 1024;
    internal const int WorktreeOutputLimit = 256 * 1024;
    private readonly GitClient _git;

    public WorkspaceProjectChangeService(GitClient? git = null)
    {
        _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromSeconds(20));
    }

    public async Task<WorkspaceProjectChangeSnapshot> InspectAsync(
        IReadOnlyList<RepositoryCatalogEntry> repositories,
        IProgress<WorkspaceProjectChangeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var changed = new Dictionary<string, IReadOnlyList<WorkspaceWorkingCopyChange>>(comparer);
        var unavailable = new HashSet<string>(comparer);
        var completed = 0;
        using var concurrency = new SemaphoreSlim(4, 4);
        var observations = await Task.WhenAll(repositories.Select(async project =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                progress?.Report(new(Volatile.Read(ref completed), repositories.Count, project.Name));
                return await InspectProjectAsync(project, comparer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
                var current = Interlocked.Increment(ref completed);
                progress?.Report(new(current, repositories.Count, project.Name));
            }
        })).ConfigureAwait(false);
        foreach (var observation in observations)
        {
            if (observation.ChangedCopies.Count > 0) changed[observation.ProjectRoot] = observation.ChangedCopies;
            foreach (var path in observation.UnavailableCopies) unavailable.Add(path);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(DateTimeOffset.UtcNow, changed, unavailable.ToArray());
    }

    private async Task<ProjectObservation> InspectProjectAsync(
        RepositoryCatalogEntry project,
        StringComparer comparer,
        CancellationToken cancellationToken)
    {
        var copies = new List<WorkspaceWorkingCopyChange>();
        var unavailable = new HashSet<string>(comparer);
        if (!WorktreeDetector.IsGitRepository(project.RootPath))
            return new(project.RootPath, copies, unavailable.ToArray());
        try
        {
            var primary = await ReadChangeCountAsync(project.RootPath, cancellationToken).ConfigureAwait(false);
            if (primary is null)
            {
                unavailable.Add(project.RootPath);
                return new(project.RootPath, copies, unavailable.ToArray());
            }
            AddIfChanged(copies, project.RootPath, primary.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException or OverflowException)
        {
            unavailable.Add(project.RootPath);
            return new(project.RootPath, copies, unavailable.ToArray());
        }

        IReadOnlyList<string>? worktrees;
        try
        {
            worktrees = await ReadWorktreesAsync(project.RootPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            worktrees = null;
        }
        if (worktrees is null)
        {
            unavailable.Add(project.RootPath);
            return new(project.RootPath, copies, unavailable.ToArray());
        }

        var seen = new HashSet<string>(comparer) { Path.GetFullPath(project.RootPath) };
        foreach (var worktree in worktrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path;
            try { path = Path.GetFullPath(worktree); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                unavailable.Add(worktree);
                continue;
            }
            if (!seen.Add(path)) continue;
            if (!Directory.Exists(path)) { unavailable.Add(path); continue; }
            try
            {
                var count = await ReadChangeCountAsync(path, cancellationToken).ConfigureAwait(false);
                if (count is null) { unavailable.Add(path); continue; }
                AddIfChanged(copies, path, count.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
            {
                unavailable.Add(path);
            }
        }
        return new(project.RootPath, copies, unavailable.ToArray());
    }

    private async Task<int?> ReadChangeCountAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _git.RunRawAsync(path,
            ["status", "--porcelain=2", "--branch", "--untracked-files=normal", "-z"],
            StatusOutputLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.StartFailed || result.TimedOut || result.StandardErrorLimitExceeded) return null;

        var count = result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Count(static record => record.StartsWith("1 ", StringComparison.Ordinal) ||
                                    record.StartsWith("2 ", StringComparison.Ordinal) ||
                                    record.StartsWith("u ", StringComparison.Ordinal) ||
                                    record.StartsWith("? ", StringComparison.Ordinal));
        return result.StandardOutputLimitExceeded ? Math.Max(1, count) : count;
    }

    private async Task<IReadOnlyList<string>?> ReadWorktreesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await _git.RunRawAsync(repositoryRoot,
            ["worktree", "list", "--porcelain", "-z"],
            WorktreeOutputLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return null;

        return result.StdOut.Split("\0\0", StringSplitOptions.RemoveEmptyEntries)
            .Select(static record => record.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            .Where(static fields => !fields.Contains("bare", StringComparer.Ordinal))
            .Select(static fields => fields.FirstOrDefault(field => field.StartsWith("worktree ", StringComparison.Ordinal)))
            .Where(static field => field is not null)
            .Select(static field => field!["worktree ".Length..])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static void AddIfChanged(List<WorkspaceWorkingCopyChange> copies, string path, int count)
    {
        if (count > 0) copies.Add(new(path, count));
    }

    private sealed record ProjectObservation(string ProjectRoot, IReadOnlyList<WorkspaceWorkingCopyChange> ChangedCopies,
        IReadOnlyList<string> UnavailableCopies);
}
