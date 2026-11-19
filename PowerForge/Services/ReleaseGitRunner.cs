namespace PowerForge;

/// <summary>
/// Shared Git plumbing for release services that read repository state, commit
/// release metadata and push it. Callers own the policy; this type only runs Git.
/// </summary>
internal sealed class ReleaseGitRunner
{
    private readonly GitClient _git;
    private readonly TimeSpan _timeout;

    internal ReleaseGitRunner(GitClient? git = null, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        _git = git ?? new GitClient(defaultTimeout: _timeout);
    }

    /// <summary>Runs Git and throws with Git's own message when it fails.</summary>
    internal ProcessRunResult Run(string repositoryRoot, params string[] arguments)
    {
        var result = RunAllowFailure(repositoryRoot, arguments);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}. {detail.Trim()}");
        }

        return result;
    }

    /// <summary>Runs Git and returns the result whatever the exit code.</summary>
    internal ProcessRunResult RunAllowFailure(string repositoryRoot, params string[] arguments)
        => _git.RunRawAsync(repositoryRoot, arguments, _timeout)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    internal string GetHeadSha(string repositoryRoot)
        => Run(repositoryRoot, "rev-parse", "HEAD").StdOut.Trim();

    /// <summary>Returns the most useful single line of a failed Git command.</summary>
    internal static string Describe(ProcessRunResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        var lines = (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("hint:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (lines.Length == 0)
            return $"exit code {result.ExitCode}";

        return lines.FirstOrDefault(static line =>
                   line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("!", StringComparison.Ordinal) ||
                   line.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
               ?? lines[0];
    }
}
