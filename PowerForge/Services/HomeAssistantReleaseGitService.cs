using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class HomeAssistantReleaseGitService {
    private readonly GitClient _git;

    internal HomeAssistantReleaseGitService(GitClient? git = null) {
        _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromMinutes(2));
    }

    internal void EnsureClean(string repositoryRoot) {
        var result = Run(repositoryRoot, "status", "--porcelain", "--untracked-files=normal");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            throw new InvalidOperationException("The release checkout must be clean before PowerForge changes version metadata.\n" + result.StdOut.Trim());
    }

    internal void EnsureContainsMerge(string repositoryRoot, string mergeCommitSha) {
        if (string.IsNullOrWhiteSpace(mergeCommitSha)) return;
        var result = RunAllowFailure(repositoryRoot, "merge-base", "--is-ancestor", mergeCommitSha, "HEAD");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"The checked-out default branch does not contain merge commit {mergeCommitSha}.");
    }

    internal string GetHeadSha(string repositoryRoot)
        => Run(repositoryRoot, "rev-parse", "HEAD").StdOut.Trim();

    internal string? FindCommitForSourcePullRequest(string repositoryRoot, int pullRequestNumber) {
        var result = RunAllowFailure(
            repositoryRoot,
            "log",
            "--all",
            "--fixed-strings",
            $"--grep=Source-PR: #{pullRequestNumber}",
            "-n",
            "1",
            "--format=%H");
        if (!result.Succeeded) return null;
        var sha = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    internal string CommitAndPush(
        string repositoryRoot,
        IReadOnlyCollection<string> changedFiles,
        string version,
        int pullRequestNumber,
        string mergeCommitSha,
        string defaultBranch) {
        if (changedFiles.Count == 0)
            return GetHeadSha(repositoryRoot);

        Run(repositoryRoot, "config", "user.name", "github-actions[bot]");
        Run(repositoryRoot, "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com");

        var addArguments = new List<string> { "add", "--" };
        addArguments.AddRange(changedFiles);
        Run(repositoryRoot, addArguments.ToArray());

        var staged = RunAllowFailure(repositoryRoot, "diff", "--cached", "--quiet");
        if (staged.ExitCode == 0)
            return GetHeadSha(repositoryRoot);
        if (staged.ExitCode != 1)
            throw new InvalidOperationException($"Unable to inspect staged release changes. {staged.StdErr.Trim()}");

        var message = $"Release v{version}\n\nSource-PR: #{pullRequestNumber}\nSource-Merge: {mergeCommitSha}";
        Run(repositoryRoot, "commit", "-m", message);
        var commitSha = GetHeadSha(repositoryRoot);
        Run(repositoryRoot, "push", "origin", $"HEAD:refs/heads/{defaultBranch}");
        return commitSha;
    }

    private ProcessRunResult Run(string repositoryRoot, params string[] arguments) {
        var result = RunAllowFailure(repositoryRoot, arguments);
        if (!result.Succeeded) {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}. {detail.Trim()}");
        }

        return result;
    }

    private ProcessRunResult RunAllowFailure(string repositoryRoot, params string[] arguments)
        => _git.RunRawAsync(repositoryRoot, arguments, TimeSpan.FromMinutes(2))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
}