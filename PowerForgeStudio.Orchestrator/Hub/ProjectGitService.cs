using PowerForge;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class ProjectGitService : IProjectHistoryService
{
    private const int MaxHistoryFiles = 400;
    private const int MaxHistoryOutputCharacters = 256 * 1024;
    private const int MaxHistoryContextCharacters = 16 * 1024;
    private readonly GitClient _gitClient;

    public ProjectGitService()
        : this(new GitClient())
    {
    }

    public ProjectGitService(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public async Task<ProjectGitStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(repositoryRoot) || !Catalog.WorktreeDetector.IsGitRepository(repositoryRoot))
            return ProjectGitStatus.NotARepository;
        var result = await _gitClient.RunRawAsync(repositoryRoot,
            ["status", "--porcelain=2", "--branch", "--untracked-files=all", "-z"], cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        var (branch, upstream, ahead, behind, staged, unstaged, untracked) = ParseFileChanges(result.StdOut);
        var branches = await GetBranchListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var worktrees = await GetWorktreeListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        return new ProjectGitStatus(true, branch, upstream, ahead, behind, staged.Count, unstaged.Count,
            untracked.Count, staged, unstaged, untracked, branches, worktrees);
    }

    public async Task<ProjectHistoryContext> GetHistoryContextAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(repositoryRoot) || !Catalog.WorktreeDetector.IsGitRepository(repositoryRoot))
            return ProjectHistoryContext.NotARepository;
        var result = await _gitClient.RunRawAsync(repositoryRoot,
            ["status", "--porcelain=2", "--branch", "--untracked-files=no", "-z"],
            MaxHistoryContextCharacters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureHistoryReadCompleted(result);
        return new ProjectHistoryContext(true, ParseHistoryBranch(result.StdOut));
    }

    public async Task<string> GetDiffAsync(string repositoryRoot, string? filePath = null, bool staged = false, CancellationToken cancellationToken = default, string? originalPath = null)
    {
        var args = new List<string> { "--literal-pathspecs", "diff", "--no-ext-diff", "--no-textconv", "--no-color" };
        if (staged) args.Add("--cached");
        if (!string.IsNullOrWhiteSpace(filePath)) { args.Add("--"); args.Add(ValidateFilePath(repositoryRoot, filePath)); }
        if (staged && originalPath is not null && !string.IsNullOrWhiteSpace(filePath)) args.Add(ValidateFilePath(repositoryRoot, originalPath));

        var result = await _gitClient.RunRawAsync(repositoryRoot, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return result.StdOut.Length > 256 * 1024 ? result.StdOut[..(256 * 1024)] + "\n[Diff truncated at 256 KiB]" : result.StdOut;
    }

    public async Task<IReadOnlyList<GitLogEntry>> GetLogAsync(string repositoryRoot, int count = 15, CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 100);
        var result = await _gitClient.RunRawAsync(
            repositoryRoot, ["log", $"-{count}", "--format=%H%n%h%n%an%n%s%n%aI%n---"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded) return [];
        return ParseLogEntries(result.StdOut);
    }

    public async Task<IReadOnlyList<GitLogEntry>> GetHistoryLogAsync(
        string repositoryRoot,
        int count = 15,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 100);
        var head = await _gitClient.RunRawAsync(repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!head.Succeeded)
        {
            EnsureExpectedAbsence(head);
            return [];
        }

        var result = await _gitClient.RunRawAsync(repositoryRoot,
            ["log", $"-{count}", "--format=%H%n%h%n%an%n%s%n%aI%n---"],
            MaxHistoryOutputCharacters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return ParseLogEntries(result.StdOut);
    }

    public async Task<GitCommitDetail> GetCommitDetailAsync(
        string repositoryRoot,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        ValidateCommitHash(commitHash);
        var parentResult = await _gitClient.RunRawAsync(repositoryRoot,
            ["rev-list", "--parents", "-n", "1", commitHash],
            MaxHistoryContextCharacters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(parentResult);
        var revisions = parentResult.StdOut.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (revisions.Length == 0 || !string.Equals(revisions[0], commitHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Git did not return the selected commit while resolving its first parent.");
        var firstParent = revisions.Length > 1 ? revisions[1] : null;
        IReadOnlyList<string> fileArguments = firstParent is null
            ? ["diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "-z", commitHash, "--"]
            : ["diff", "--name-only", "-z", "--no-ext-diff", "--no-textconv", firstParent, commitHash, "--"];
        var filesResult = await _gitClient.RunRawAsync(repositoryRoot,
            fileArguments,
            MaxHistoryOutputCharacters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureHistoryReadCompleted(filesResult);
        var fileOutput = filesResult.StdOut;
        if (filesResult.StandardOutputLimitExceeded && fileOutput.Length > 0 && fileOutput[^1] != '\0')
        {
            var lastCompletePath = fileOutput.LastIndexOf('\0');
            fileOutput = lastCompletePath < 0 ? "" : fileOutput[..(lastCompletePath + 1)];
        }
        var changedFiles = fileOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxHistoryFiles)
            .ToArray();
        var filesTruncated = filesResult.StandardOutputLimitExceeded ||
                             filesResult.StdOut.Count(character => character == '\0') > MaxHistoryFiles;

        IReadOnlyList<string> diffArguments = firstParent is null
            ? ["show", "--format=", "--no-ext-diff", "--no-textconv", "--no-color", "--find-renames", "--find-copies", commitHash, "--"]
            : ["diff", "--no-ext-diff", "--no-textconv", "--no-color", "--find-renames", "--find-copies", firstParent, commitHash, "--"];
        var diffResult = await _gitClient.RunRawAsync(repositoryRoot,
            diffArguments,
            MaxHistoryOutputCharacters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureHistoryReadCompleted(diffResult);
        var diff = string.IsNullOrWhiteSpace(diffResult.StdOut)
            ? "No textual diff is available for this commit."
            : diffResult.StdOut;
        if (diffResult.StandardOutputLimitExceeded)
            diff += "\n[Commit diff truncated at 256K characters]";

        return new GitCommitDetail(commitHash, changedFiles, diff, filesTruncated, diffResult.StandardOutputLimitExceeded);
    }

    public Task<bool> StageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default)
        => RunMutationAsync(repositoryRoot, ["--literal-pathspecs", "add", "--", ValidateFilePath(repositoryRoot, filePath)], cancellationToken);

    public async Task<bool> UnstageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default, string? originalPath = null)
    {
        var path = ValidateFilePath(repositoryRoot, filePath);
        var head = await _gitClient.RunRawAsync(repositoryRoot, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!head.Succeeded)
        {
            // Only a genuinely unborn branch may use index removal. A failed HEAD
            // probe (including a missing object or process failure) is not permission.
            EnsureExpectedAbsence(head);
            var symbolic = await _gitClient.RunRawAsync(repositoryRoot, ["symbolic-ref", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false);
            EnsureSuccess(symbolic);
            var reference = symbolic.StdOut.Trim();
            if (!reference.StartsWith("refs/heads/", StringComparison.Ordinal))
                throw new InvalidOperationException("Cannot unstage: HEAD does not identify a local branch.");
            var exists = await _gitClient.RunRawAsync(repositoryRoot, ["show-ref", "--verify", "--quiet", reference], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (exists.Succeeded)
                throw new InvalidOperationException("Cannot unstage: the branch exists but HEAD could not be resolved. Inspect repository integrity.");
            EnsureExpectedAbsence(exists);
        }
        var paths = originalPath is null ? new[] { path } : new[] { path, ValidateFilePath(repositoryRoot, originalPath) };
        return await RunMutationAsync(repositoryRoot, head.Succeeded
            ? ["--literal-pathspecs", "restore", "--staged", "--", .. paths]
            : ["--literal-pathspecs", "rm", "--cached", "-f", "--", .. paths], cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => RunMutationAsync(repositoryRoot, ["add", "-A"], cancellationToken);

    public Task<bool> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunMutationAsync(repositoryRoot, ["commit", "-m", message], cancellationToken);
    }

    private async Task<bool> RunMutationAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _gitClient.RunRawAsync(root, arguments, timeout: TimeSpan.FromMinutes(2), cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return true;
    }

    private static void EnsureSuccess(ProcessRunResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(result.TimedOut ? "Git timed out. Refresh repository state before retrying."
                : result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded ? "Git output exceeded the capture limit."
                : "Git failed: " + Host.StudioOutputSanitizer.Sanitize(result.StdErr));
    }

    private static void EnsureHistoryReadCompleted(ProcessRunResult result)
    {
        if (result.ExitCode != 0 || result.StartFailed || result.TimedOut || result.StandardErrorLimitExceeded)
            EnsureSuccess(result);
    }

    private static IReadOnlyList<GitLogEntry> ParseLogEntries(string output)
    {
        var entries = new List<GitLogEntry>();
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None);
        var i = 0;
        while (i + 4 < lines.Length)
        {
            var hash = lines[i].Trim();
            var shortHash = lines[i + 1].Trim();
            var author = lines[i + 2].Trim();
            var message = lines[i + 3].Trim();
            var dateStr = lines[i + 4].Trim();
            if (!string.IsNullOrEmpty(hash) && DateTimeOffset.TryParse(dateStr, out var date))
                entries.Add(new GitLogEntry(hash, shortHash, author, message, date));
            i += 5;
            while (i < lines.Length && lines[i].Trim() == "---") i++;
        }
        return entries;
    }

    private static string ParseHistoryBranch(string output)
    {
        const string prefix = "# branch.head ";
        var start = output.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return "";
        start += prefix.Length;
        var end = output.IndexOfAny(['\r', '\n', '\0'], start);
        var branch = (end < 0 ? output[start..] : output[start..end]).Trim();
        return branch == "(detached)" ? "" : branch;
    }

    private static void ValidateCommitHash(string commitHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        if (commitHash.Length is not (40 or 64) || !commitHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Select a valid commit from the observed history.", nameof(commitHash));
    }

    private static void EnsureExpectedAbsence(ProcessRunResult result)
    {
        if (result.ExitCode != 1 || result.StartFailed || result.TimedOut || result.StandardOutputLimitExceeded
            || result.StandardErrorLimitExceeded || !string.IsNullOrWhiteSpace(result.StdErr))
            EnsureSuccess(result);
    }

    private static string ValidateFilePath(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path, Path.GetFullPath(root)));
        if (relative == "." || relative == ".." || Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Select a file inside this working copy.", nameof(path));
        return relative;
    }

    public async Task<bool> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var normalized = await ValidateBranchNameAsync(repositoryRoot, branchName, cancellationToken).ConfigureAwait(false);
        return await RunMutationAsync(repositoryRoot, ["switch", "-c", normalized], cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SwitchBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var normalized = await ValidateBranchNameAsync(repositoryRoot, branchName, cancellationToken).ConfigureAwait(false);
        var exists = await _gitClient.RunRawAsync(repositoryRoot,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{normalized}"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!exists.Succeeded)
        {
            EnsureExpectedAbsence(exists);
            throw new ArgumentException("Select an existing local branch.", nameof(branchName));
        }
        return await RunMutationAsync(repositoryRoot, ["switch", normalized], cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ValidateBranchNameAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        var normalized = branchName.Trim();
        if (normalized.StartsWith("-", StringComparison.Ordinal))
            throw new ArgumentException("Branch names cannot start with a dash.", nameof(branchName));
        var result = await _gitClient.RunRawAsync(repositoryRoot,
            ["check-ref-format", $"refs/heads/{normalized}"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            EnsureExpectedAbsence(result);
            throw new ArgumentException("Enter a valid Git branch name.", nameof(branchName));
        }
        return normalized;
    }

    public async Task<bool> CreateWorktreeAsync(string repositoryRoot, string path, string branchName, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "add", path, "-b", branchName], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> RemoveWorktreeAsync(string repositoryRoot, string path, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "remove", path], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    private async Task<IReadOnlyList<string>> GetBranchListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await _gitClient.RunRawAsync(repositoryRoot, ["branch", "--format=%(refname:short)"], cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);

        return result.StdOut
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private async Task<IReadOnlyList<GitWorktreeEntry>> GetWorktreeListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return ParseWorktreeList(result.StdOut);
    }

    private static IReadOnlyList<GitWorktreeEntry> ParseWorktreeList(string output)
    {
        var entries = new List<GitWorktreeEntry>();
        string? path = null;
        string? branch = null;
        var isLocked = false;
        var isBare = false;

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (path is not null)
                {
                    entries.Add(new GitWorktreeEntry(path, branch, isLocked, isBare));
                    path = null; branch = null; isLocked = false; isBare = false;
                }
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var fullRef = line["branch ".Length..];
                branch = fullRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? fullRef["refs/heads/".Length..] : fullRef;
            }
            else if (line == "locked") isLocked = true;
            else if (line == "bare") isBare = true;
        }

        if (path is not null)
            entries.Add(new GitWorktreeEntry(path, branch, isLocked, isBare));

        return entries;
    }
}
