using PowerForge;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class ProjectGitService
{
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

        if (!Directory.Exists(repositoryRoot))
        {
            return ProjectGitStatus.NotARepository;
        }

        try
        {
            // Use GitClient's existing porcelain status parsing for core data
            var snapshot = await _gitClient.GetStatusAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
            if (!snapshot.IsGitRepository)
            {
                return ProjectGitStatus.NotARepository;
            }

            // Get detailed file changes via git status --short (extends GitClient's data)
            var shortResult = await _gitClient.RunRawAsync(
                repositoryRoot, ["status", "--porcelain=2", "--branch"],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            List<GitFileChange> staged;
            List<GitFileChange> unstaged;
            List<GitFileChange> untracked;

            if (shortResult.Succeeded)
            {
                (_, _, _, _, staged, unstaged, untracked) = ParseFileChanges(shortResult.StdOut);
            }
            else
            {
                staged = []; unstaged = []; untracked = [];
            }

            var branches = await GetBranchListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
            var worktrees = await GetWorktreeListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);

            return new ProjectGitStatus(
                IsGitRepository: true,
                BranchName: snapshot.BranchName,
                UpstreamBranch: snapshot.UpstreamBranch,
                AheadCount: snapshot.AheadCount,
                BehindCount: snapshot.BehindCount,
                StagedCount: staged.Count,
                UnstagedCount: unstaged.Count,
                UntrackedCount: untracked.Count,
                StagedChanges: staged,
                UnstagedChanges: unstaged,
                UntrackedFiles: untracked,
                Branches: branches,
                Worktrees: worktrees);
        }
        catch
        {
            return ProjectGitStatus.NotARepository;
        }
    }

    public async Task<string> GetDiffAsync(string repositoryRoot, string? filePath = null, bool staged = false, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "diff" };
        if (staged) args.Add("--cached");
        if (!string.IsNullOrWhiteSpace(filePath)) { args.Add("--"); args.Add(filePath); }

        var result = await _gitClient.RunRawAsync(repositoryRoot, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StdOut : string.Empty;
    }

    public async Task<IReadOnlyList<GitLogEntry>> GetLogAsync(string repositoryRoot, int count = 15, CancellationToken cancellationToken = default)
    {
        var result = await _gitClient.RunRawAsync(
            repositoryRoot, ["log", $"-{count}", "--format=%H%n%h%n%an%n%s%n%aI%n---"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded) return [];

        var entries = new List<GitLogEntry>();
        var lines = result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.None);
        var i = 0;
        while (i + 4 < lines.Length)
        {
            var hash = lines[i].Trim();
            var shortHash = lines[i + 1].Trim();
            var author = lines[i + 2].Trim();
            var message = lines[i + 3].Trim();
            var dateStr = lines[i + 4].Trim();

            if (!string.IsNullOrEmpty(hash) && DateTimeOffset.TryParse(dateStr, out var date))
            {
                entries.Add(new GitLogEntry(hash, shortHash, author, message, date));
            }

            i += 5;
            while (i < lines.Length && lines[i].Trim() == "---") i++;
        }

        return entries;
    }

    public async Task<bool> StageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["add", "--", filePath], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> UnstageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["restore", "--staged", "--", filePath], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["add", "-A"], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["commit", "-m", message], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
        => (await _gitClient.CreateBranchAsync(repositoryRoot, branchName, cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> SwitchBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["switch", branchName], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> CreateWorktreeAsync(string repositoryRoot, string path, string branchName, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "add", path, "-b", branchName], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> RemoveWorktreeAsync(string repositoryRoot, string path, CancellationToken cancellationToken = default)
        => (await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "remove", path], cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    private async Task<IReadOnlyList<string>> GetBranchListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await _gitClient.RunRawAsync(repositoryRoot, ["branch", "--format=%(refname:short)"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return [];

        return result.StdOut
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private async Task<IReadOnlyList<GitWorktreeEntry>> GetWorktreeListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await _gitClient.RunRawAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return [];
        return ParseWorktreeList(result.StdOut);
    }

    // File-change parsing extends GitClient's aggregate counts with per-file detail
    private static (string? BranchName, string? Upstream, int Ahead, int Behind,
        List<GitFileChange> Staged, List<GitFileChange> Unstaged, List<GitFileChange> Untracked)
        ParseFileChanges(string output)
    {
        string? branchName = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var staged = new List<GitFileChange>();
        var unstaged = new List<GitFileChange>();
        var untracked = new List<GitFileChange>();

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.", StringComparison.Ordinal)) continue; // Skip header lines (GitClient handles these)
            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked.Add(new GitFileChange(line[2..], GitChangeKind.Untracked));
                continue;
            }

            if (line.StartsWith("1 ", StringComparison.Ordinal) && line.Length > 4)
            {
                var xy = line.Substring(2, 2);
                var pathStart = FindNthSpace(line, 8);
                var path = pathStart >= 0 ? line[(pathStart + 1)..] : line;
                if (xy[0] != '.') staged.Add(new GitFileChange(path, ParseChangeChar(xy[0])));
                if (xy[1] != '.') unstaged.Add(new GitFileChange(path, ParseChangeChar(xy[1])));
                continue;
            }

            if (line.StartsWith("2 ", StringComparison.Ordinal) && line.Length > 4)
            {
                var xy = line.Substring(2, 2);
                var tabIndex = line.IndexOf('\t');
                var path = tabIndex >= 0 ? line[(tabIndex + 1)..] : line;
                if (xy[0] != '.') staged.Add(new GitFileChange(path, GitChangeKind.Renamed));
                if (xy[1] != '.') unstaged.Add(new GitFileChange(path, GitChangeKind.Renamed));
            }
        }

        return (branchName, upstream, ahead, behind, staged, unstaged, untracked);
    }

    private static GitChangeKind ParseChangeChar(char c) => c switch
    {
        'A' => GitChangeKind.Added, 'M' => GitChangeKind.Modified, 'D' => GitChangeKind.Deleted,
        'R' => GitChangeKind.Renamed, 'C' => GitChangeKind.Copied, _ => GitChangeKind.Modified
    };

    private static int FindNthSpace(string text, int n)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && ++count == n) return i;
        }
        return -1;
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
