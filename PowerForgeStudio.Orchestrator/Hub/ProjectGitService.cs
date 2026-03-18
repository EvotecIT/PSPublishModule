using PowerForge;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class ProjectGitService
{
    private readonly IProcessRunner _processRunner;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public ProjectGitService()
        : this(new ProcessRunner())
    {
    }

    public ProjectGitService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
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
            var statusResult = await RunGitAsync(repositoryRoot, ["status", "--porcelain=2", "--branch"], cancellationToken).ConfigureAwait(false);
            if (!statusResult.Succeeded)
            {
                return ProjectGitStatus.NotARepository;
            }

            var (branchName, upstream, ahead, behind, staged, unstaged, untracked) = ParsePorcelainStatus(statusResult.StdOut);
            var branches = await GetBranchListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
            var worktrees = await GetWorktreeListAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);

            return new ProjectGitStatus(
                IsGitRepository: true,
                BranchName: branchName,
                UpstreamBranch: upstream,
                AheadCount: ahead,
                BehindCount: behind,
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
        if (staged)
        {
            args.Add("--cached");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            args.Add("--");
            args.Add(filePath);
        }

        var result = await RunGitAsync(repositoryRoot, args, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StdOut : string.Empty;
    }

    public async Task<bool> StageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["add", "--", filePath], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> UnstageFileAsync(string repositoryRoot, string filePath, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["restore", "--staged", "--", filePath], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["add", "-A"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["commit", "-m", message], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["switch", "-c", branchName], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> SwitchBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["switch", branchName], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> CreateWorktreeAsync(string repositoryRoot, string path, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["worktree", "add", path, "-b", branchName], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> RemoveWorktreeAsync(string repositoryRoot, string path, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(repositoryRoot, ["worktree", "remove", path], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private async Task<IReadOnlyList<string>> GetBranchListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repositoryRoot, ["branch", "--format=%(refname:short)"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return [];
        }

        return result.StdOut
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private async Task<IReadOnlyList<GitWorktreeEntry>> GetWorktreeListAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return [];
        }

        return ParseWorktreeList(result.StdOut);
    }

    private Task<ProcessRunResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            new ProcessRunRequest("git", workingDirectory, arguments, DefaultTimeout),
            cancellationToken);

    private static (string? BranchName, string? Upstream, int Ahead, int Behind,
        List<GitFileChange> Staged, List<GitFileChange> Unstaged, List<GitFileChange> Untracked)
        ParsePorcelainStatus(string output)
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
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branchName = line["# branch.head ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = line["# branch.upstream ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                ParseAheadBehind(line["# branch.ab ".Length..], out ahead, out behind);
                continue;
            }

            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                var path = line[2..];
                untracked.Add(new GitFileChange(path, GitChangeKind.Untracked));
                continue;
            }

            // Ordinary changed entry: "1 XY ..."
            if (line.StartsWith("1 ", StringComparison.Ordinal) && line.Length > 4)
            {
                var xy = line.Substring(2, 2);
                // Path starts after "1 XY sub mH mI mW hH hI "
                var pathStart = FindNthSpace(line, 8);
                var path = pathStart >= 0 ? line[(pathStart + 1)..] : line;

                if (xy[0] != '.')
                {
                    staged.Add(new GitFileChange(path, ParseChangeChar(xy[0])));
                }

                if (xy[1] != '.')
                {
                    unstaged.Add(new GitFileChange(path, ParseChangeChar(xy[1])));
                }

                continue;
            }

            // Rename/copy entry: "2 XY ..."
            if (line.StartsWith("2 ", StringComparison.Ordinal) && line.Length > 4)
            {
                var xy = line.Substring(2, 2);
                var tabIndex = line.IndexOf('\t');
                var path = tabIndex >= 0 ? line[(tabIndex + 1)..] : line;

                if (xy[0] != '.')
                {
                    staged.Add(new GitFileChange(path, GitChangeKind.Renamed));
                }

                if (xy[1] != '.')
                {
                    unstaged.Add(new GitFileChange(path, GitChangeKind.Renamed));
                }
            }
        }

        return (branchName, upstream, ahead, behind, staged, unstaged, untracked);
    }

    private static void ParseAheadBehind(string value, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith('+') && int.TryParse(part[1..], out var a))
            {
                ahead = a;
            }

            if (part.StartsWith('-') && int.TryParse(part[1..], out var b))
            {
                behind = b;
            }
        }
    }

    private static GitChangeKind ParseChangeChar(char c) => c switch
    {
        'A' => GitChangeKind.Added,
        'M' => GitChangeKind.Modified,
        'D' => GitChangeKind.Deleted,
        'R' => GitChangeKind.Renamed,
        'C' => GitChangeKind.Copied,
        _ => GitChangeKind.Modified
    };

    private static int FindNthSpace(string text, int n)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                count++;
                if (count == n)
                {
                    return i;
                }
            }
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
                    path = null;
                    branch = null;
                    isLocked = false;
                    isBare = false;
                }

                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var fullRef = line["branch ".Length..];
                branch = fullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? fullRef["refs/heads/".Length..]
                    : fullRef;
            }
            else if (line == "locked")
            {
                isLocked = true;
            }
            else if (line == "bare")
            {
                isBare = true;
            }
        }

        if (path is not null)
        {
            entries.Add(new GitWorktreeEntry(path, branch, isLocked, isBare));
        }

        return entries;
    }
}
