using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PowerForge;

internal sealed class HomeAssistantReleaseGitService {
    private readonly GitClient _git;
    private readonly IProcessRunner _processRunner;

    internal HomeAssistantReleaseGitService(GitClient? git = null, IProcessRunner? processRunner = null) {
        _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromMinutes(2));
        _processRunner = processRunner ?? new ProcessRunner();
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
            "HEAD",
            "--fixed-strings",
            $"--grep=Source-PR: #{pullRequestNumber}",
            "-n",
            "1",
            "--format=%H");
        if (!result.Succeeded) return null;
        var sha = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    internal void EnsureNoTrackedChanges(string repositoryRoot) {
        var result = Run(repositoryRoot, "status", "--porcelain", "--untracked-files=no");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            throw new InvalidOperationException("The release build changed tracked files after the immutable release commit was created.\n" + result.StdOut.Trim());
    }

    internal string CommitRelease(
        string repositoryRoot,
        IReadOnlyCollection<string> changedFiles,
        string version,
        int pullRequestNumber,
        string mergeCommitSha) {
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
        return GetHeadSha(repositoryRoot);
    }

    internal void Push(
        string repositoryRoot,
        string defaultBranch,
        string token,
        string owner,
        string repository,
        string serverUrl) {
        if (string.IsNullOrWhiteSpace(defaultBranch)) throw new ArgumentException("Default branch is required.", nameof(defaultBranch));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("GitHub token is required.", nameof(token));
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Repository owner is required.", nameof(owner));
        if (string.IsNullOrWhiteSpace(repository)) throw new ArgumentException("Repository name is required.", nameof(repository));
        if (!Uri.TryCreate(serverUrl?.TrimEnd('/'), UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("GitHub server URL must be an absolute HTTPS address.", nameof(serverUrl));

        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token));
        var pushUrl = new Uri(server, $"{owner}/{repository}.git").AbsoluteUri;
        var emptyHooksDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.HomeAssistant.EmptyHooks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyHooksDirectory);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            ["GH_TOKEN"] = null,
            ["GITHUB_TOKEN"] = null,
            ["GIT_CONFIG_COUNT"] = "4",
            ["GIT_CONFIG_KEY_0"] = "credential.helper",
            ["GIT_CONFIG_VALUE_0"] = string.Empty,
            ["GIT_CONFIG_KEY_1"] = "core.hooksPath",
            ["GIT_CONFIG_VALUE_1"] = emptyHooksDirectory,
            ["GIT_CONFIG_KEY_2"] = "http.followRedirects",
            ["GIT_CONFIG_VALUE_2"] = "false",
            ["GIT_CONFIG_KEY_3"] = $"http.{pushUrl}.extraheader",
            ["GIT_CONFIG_VALUE_3"] = "AUTHORIZATION: basic " + basicToken,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_TERMINAL_PROMPT"] = "0"
        };
        try {
            var result = _processRunner.RunAsync(new ProcessRunRequest(
                    "git",
                    repositoryRoot,
                    new[] { "push", pushUrl, $"HEAD:refs/heads/{defaultBranch}" },
                    TimeSpan.FromMinutes(2),
                    environment))
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (!result.Succeeded) {
                var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                throw new InvalidOperationException($"git push failed with exit code {result.ExitCode}. {detail.Trim()}");
            }
        } finally {
            if (Directory.Exists(emptyHooksDirectory)) Directory.Delete(emptyHooksDirectory, recursive: true);
        }
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
