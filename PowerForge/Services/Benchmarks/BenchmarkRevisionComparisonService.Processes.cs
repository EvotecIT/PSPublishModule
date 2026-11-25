namespace PowerForge;

public sealed partial class BenchmarkRevisionComparisonService
{
    private const int LogTailLines = 30;

    private async Task<string> GitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await _processRunner.RunAsync(
            new ProcessRunRequest("git", workingDirectory, arguments, TimeSpan.FromMinutes(5)),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed ({result.ExitCode}): {FirstNonEmpty(result.StdErr, result.StdOut)}");
        return result.StdOut.Trim();
    }

    private async Task<string> DefaultBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var remoteHead = await _processRunner.RunAsync(
            new ProcessRunRequest("git", repositoryRoot, new[] { "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD" }, TimeSpan.FromMinutes(1)),
            cancellationToken).ConfigureAwait(false);
        string head = remoteHead.StdOut.Trim();
        if (remoteHead.ExitCode == 0 && head.Length > 0)
            return head.StartsWith("origin/", StringComparison.Ordinal) ? head.Substring("origin/".Length) : head;

        foreach (string candidate in new[] { "main", "master" })
        {
            var exists = await _processRunner.RunAsync(
                new ProcessRunRequest("git", repositoryRoot, new[] { "rev-parse", "--verify", "--quiet", "refs/heads/" + candidate }, TimeSpan.FromMinutes(1)),
                cancellationToken).ConfigureAwait(false);
            if (exists.ExitCode == 0) return candidate;
        }
        throw new InvalidOperationException("Could not find the repository's default branch. Pass the baseline revision explicitly.");
    }

    private async Task<string> ResolveCommitAsync(string repositoryRoot, string revision, CancellationToken cancellationToken)
    {
        if (revision.StartsWith("-", StringComparison.Ordinal))
            throw new ArgumentException($"Revision '{revision}' is not valid.");
        return await GitAsync(repositoryRoot, cancellationToken, "rev-parse", "--verify", "--quiet", revision + "^{commit}").ConfigureAwait(false);
    }

    private async Task<string> AddWorktreeAsync(
        string repositoryRoot,
        string path,
        string commit,
        List<string> worktrees,
        CancellationToken cancellationToken)
    {
        await GitAsync(repositoryRoot, cancellationToken, "worktree", "add", "--detach", path, commit).ConfigureAwait(false);
        worktrees.Add(path);
        return path;
    }

    private async Task RemoveWorktreeAsync(string repositoryRoot, string path)
    {
        try
        {
            var removed = await _processRunner.RunAsync(
                new ProcessRunRequest("git", repositoryRoot, new[] { "worktree", "remove", "--force", path }, TimeSpan.FromMinutes(5)),
                CancellationToken.None).ConfigureAwait(false);
            if (removed.ExitCode == 0 && !Directory.Exists(path)) return;

            TryDeleteDirectory(path);
            await _processRunner.RunAsync(
                new ProcessRunRequest("git", repositoryRoot, new[] { "worktree", "prune" }, TimeSpan.FromMinutes(5)),
                CancellationToken.None).ConfigureAwait(false);
            if (Directory.Exists(path))
                _logger.Warn($"Could not remove the temporary worktree {path}. Remove it with 'git worktree remove --force'.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.Warn($"Could not remove the temporary worktree {path}: {exception.Message}");
        }
    }

    private async Task BuildAsync(
        BenchmarkRevisionComparisonSpec spec,
        string root,
        string project,
        string logPath,
        string label,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Building {label} ({project}, {spec.Framework}).");
        var result = await RunLoggedAsync(root, logPath,
            new[] { "build", Path.Combine(root, project), "-c", spec.Configuration, "-f", spec.Framework, "--nologo" },
            spec.RunTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Building the {label} benchmark project failed ({result.ExitCode}). Last output:{Environment.NewLine}{result.Tail}");
    }

    private async Task<BenchmarkRunResult?> RunBenchmarkAsync(
        BenchmarkRevisionComparisonSpec spec,
        string root,
        string project,
        string[] benchmarkArguments,
        string artifacts,
        string logPath,
        string label,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Running {label}.");
        var arguments = new List<string>
        {
            "run", "--no-build", "-c", spec.Configuration, "-f", spec.Framework,
            "--project", Path.Combine(root, project), "--",
            "--filter", spec.Filter
        };
        arguments.AddRange(benchmarkArguments);
        arguments.Add("--artifacts");
        arguments.Add(artifacts);
        var result = await RunLoggedAsync(Path.GetDirectoryName(Path.Combine(root, project))!, logPath, arguments, spec.RunTimeout, cancellationToken)
            .ConfigureAwait(false);

        string results = Path.Combine(artifacts, "results");
        BenchmarkRunResult? imported = null;
        if (Directory.Exists(results) && Directory.GetFiles(results, "*-report*.json", SearchOption.AllDirectories).Length > 0)
            imported = new BenchmarkResultImporter().Import(results);

        if (result.ExitCode != 0)
            _logger.Warn($"{label} exited with {result.ExitCode}; failed cases are reported as missing. Last output:{Environment.NewLine}{result.Tail}");
        else if (imported is null)
            _logger.Warn($"{label} produced no BenchmarkDotNet JSON report. Last output:{Environment.NewLine}{result.Tail}");
        return imported;
    }

    private async Task<(int ExitCode, string Tail)> RunLoggedAsync(
        string workingDirectory,
        string logPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var tail = new Queue<string>();
        var sync = new object();
        using var log = new StreamWriter(logPath, append: false);
        void Write(string line)
        {
            lock (sync)
            {
                log.WriteLine(line);
                tail.Enqueue(line);
                while (tail.Count > LogTailLines) tail.Dequeue();
            }
            _logger.Verbose(line);
        }

        log.WriteLine("dotnet " + string.Join(" ", arguments));
        var request = new ProcessRunRequest("dotnet", workingDirectory, arguments, timeout,
            environmentVariables: null, captureOutput: true, captureError: true,
            outputLineReceived: Write, errorLineReceived: Write)
        {
            MaxCapturedOutputCharacters = 4096
        };
        var result = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            string text = string.Join(Environment.NewLine, tail);
            if (result.TimedOut) text += Environment.NewLine + $"Timed out after {timeout}.";
            if (result.StartFailed) text += Environment.NewLine + result.StdErr;
            return (result.ExitCode, text);
        }
    }

    private static string FirstNonEmpty(params string[] values)
        => values.Select(value => value?.Trim() ?? string.Empty).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
}
