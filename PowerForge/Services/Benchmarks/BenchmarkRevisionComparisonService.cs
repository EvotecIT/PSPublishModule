using System.Globalization;

namespace PowerForge;

/// <summary>
/// Compares a BenchmarkDotNet benchmark between two git revisions of one repository on one machine.
/// The baseline is checked out in a temporary worktree; the sides run interleaved, alternating which goes first.
/// </summary>
public sealed partial class BenchmarkRevisionComparisonService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<double?>> _loadAverage;
    private readonly TimeSpan _pollInterval;

    /// <summary>Creates the service with the default process runner.</summary>
    /// <param name="logger">Progress logger.</param>
    public BenchmarkRevisionComparisonService(ILogger logger)
        : this(new ProcessRunner(), logger, loadAverage: null, pollInterval: null)
    {
    }

    internal BenchmarkRevisionComparisonService(
        IProcessRunner processRunner,
        ILogger logger,
        Func<CancellationToken, Task<double?>>? loadAverage,
        TimeSpan? pollInterval)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loadAverage = loadAverage ?? (token => SystemLoadAverage.ReadFifteenMinuteAsync(_processRunner, token));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Runs the comparison.
    /// </summary>
    /// <param name="spec">Comparison settings.</param>
    /// <param name="cancellationToken">Cancels the comparison; the worktree and lock are still cleaned up.</param>
    /// <returns>The aggregated comparison.</returns>
    public async Task<BenchmarkRevisionComparisonResult> RunAsync(
        BenchmarkRevisionComparisonSpec spec,
        CancellationToken cancellationToken = default)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Project)) throw new ArgumentException("A benchmark project is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Filter)) throw new ArgumentException("A BenchmarkDotNet filter is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Framework)) throw new ArgumentException("A target framework is required.", nameof(spec));
        if (spec.Rounds < 1) throw new ArgumentException("At least one round is required.", nameof(spec));
        string[] benchmarkArguments = BenchmarkRevisionComparison.MergeBenchmarkArguments(spec.BenchmarkArguments);

        var started = DateTimeOffset.UtcNow;
        string startDirectory = string.IsNullOrWhiteSpace(spec.RepositoryPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(spec.RepositoryPath);
        string repositoryRoot = Path.GetFullPath(await GitAsync(startDirectory, cancellationToken, "rev-parse", "--show-toplevel").ConfigureAwait(false));
        string project = RelativeProject(repositoryRoot, spec.Project);

        string baselineRevision = string.IsNullOrWhiteSpace(spec.Baseline)
            ? await DefaultBranchAsync(repositoryRoot, cancellationToken).ConfigureAwait(false)
            : spec.Baseline!.Trim();
        var baseline = new BenchmarkRevisionSide
        {
            Name = BenchmarkRevisionComparison.BaselineName,
            Revision = baselineRevision,
            Commit = await ResolveCommitAsync(repositoryRoot, baselineRevision, cancellationToken).ConfigureAwait(false)
        };
        bool candidateIsWorkingTree = string.IsNullOrWhiteSpace(spec.Candidate);
        var candidate = new BenchmarkRevisionSide
        {
            Name = BenchmarkRevisionComparison.CandidateName,
            Revision = candidateIsWorkingTree ? "working tree" : spec.Candidate!.Trim(),
            Commit = await ResolveCommitAsync(repositoryRoot, candidateIsWorkingTree ? "HEAD" : spec.Candidate!.Trim(), cancellationToken).ConfigureAwait(false)
        };
        if (candidateIsWorkingTree)
        {
            string status = await GitAsync(repositoryRoot, cancellationToken, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
            candidate.Dirty = status.Length > 0;
        }

        string scratchRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(spec.ScratchRoot) ? Path.GetTempPath() : spec.ScratchRoot!);
        string workRoot = Path.Combine(scratchRoot,
            "powerforge-bench-" + started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(workRoot);
        var worktrees = new List<string>();
        try
        {
            var roots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [baseline.Name] = await AddWorktreeAsync(repositoryRoot, Path.Combine(workRoot, "baseline"), baseline.Commit, worktrees, cancellationToken).ConfigureAwait(false),
                [candidate.Name] = candidateIsWorkingTree
                    ? repositoryRoot
                    : await AddWorktreeAsync(repositoryRoot, Path.Combine(workRoot, "candidate"), candidate.Commit, worktrees, cancellationToken).ConfigureAwait(false)
            };
            foreach (var side in roots)
            {
                if (!File.Exists(Path.Combine(side.Value, project)))
                    throw new FileNotFoundException($"The {side.Key} revision has no benchmark project at {project}.", Path.Combine(side.Value, project));
            }

            using var benchmarkLock = string.IsNullOrWhiteSpace(spec.LockPath)
                ? null
                : await AcquireLockAsync(spec, baseline, candidate, cancellationToken).ConfigureAwait(false);

            foreach (var side in new[] { baseline.Name, candidate.Name })
                await BuildAsync(spec, roots[side], project, Path.Combine(workRoot, "logs", side + "-build.log"), side, cancellationToken).ConfigureAwait(false);

            var results = new Dictionary<string, List<BenchmarkRunResult?>>(StringComparer.Ordinal)
            {
                [baseline.Name] = new(),
                [candidate.Name] = new()
            };
            var order = new List<string>();
            double? loadLimit = spec.MaxLoadAverage;
            BenchmarkEnvironmentInfo? environment = null;
            BenchmarkRunnerInfo? reportedRunner = null;
            for (int round = 1; round <= spec.Rounds; round++)
            {
                foreach (string side in BenchmarkRevisionComparison.RoundOrder(round))
                {
                    loadLimit = await WaitForLoadAsync(loadLimit, spec.LoadWaitTimeout, cancellationToken).ConfigureAwait(false);
                    order.Add(round.ToString(CultureInfo.InvariantCulture) + ":" + side);
                    string artifacts = Path.Combine(workRoot, "artifacts", "round-" + round.ToString(CultureInfo.InvariantCulture) + "-" + side);
                    var result = await RunBenchmarkAsync(spec, roots[side], project, benchmarkArguments, artifacts,
                        Path.Combine(workRoot, "logs", "round-" + round.ToString(CultureInfo.InvariantCulture) + "-" + side + ".log"),
                        $"round {round}/{spec.Rounds} {side}", cancellationToken).ConfigureAwait(false);
                    results[side].Add(result);
                    environment ??= result?.Environment;
                    reportedRunner ??= result?.Runner;
                }
            }

            var comparison = new BenchmarkRevisionComparisonResult
            {
                Baseline = baseline,
                Candidate = candidate,
                Project = project.Replace('\\', '/'),
                Filter = spec.Filter,
                Framework = spec.Framework,
                Rounds = spec.Rounds,
                RunOrder = order.ToArray(),
                BenchmarkArguments = benchmarkArguments,
                Runner = new BenchmarkRunnerInfo
                {
                    Kind = "benchmarkdotnet",
                    Version = reportedRunner?.Version ?? string.Empty,
                    Statistic = "median",
                    Warmups = reportedRunner?.Warmups,
                    Iterations = reportedRunner?.Iterations,
                    Launches = reportedRunner?.Launches,
                    Ordering = "alternating",
                    InProcess = false
                },
                Environment = environment ?? new BenchmarkEnvironmentInfo(),
                Benchmarks = BenchmarkRevisionComparison.Aggregate(results[baseline.Name], results[candidate.Name]),
                ArtifactsPath = spec.KeepArtifacts ? workRoot : null,
                StartedUtc = started,
                FinishedUtc = DateTimeOffset.UtcNow
            };
            return comparison;
        }
        finally
        {
            foreach (string worktree in worktrees)
                await RemoveWorktreeAsync(repositoryRoot, worktree).ConfigureAwait(false);
            if (!spec.KeepArtifacts)
                TryDeleteDirectory(workRoot);
        }
    }

    private async Task<BenchmarkLockDirectory> AcquireLockAsync(
        BenchmarkRevisionComparisonSpec spec,
        BenchmarkRevisionSide baseline,
        BenchmarkRevisionSide candidate,
        CancellationToken cancellationToken)
    {
        string description = $"powerforge benchmark compare {spec.Filter} {baseline.Revision} vs {candidate.Revision}";
        var held = await BenchmarkLockDirectory.AcquireAsync(spec.LockPath!, description, spec.LockTimeout, _pollInterval,
            owner => _logger.Info($"Waiting for benchmark lock {spec.LockPath}: {owner}"), cancellationToken).ConfigureAwait(false);
        _logger.Info($"Holding benchmark lock {held.Path}.");
        return held;
    }

    /// <summary>
    /// Waits until the 15-minute load average is below the limit. Returns the limit to keep using, or null when the
    /// platform cannot report a load average.
    /// </summary>
    private async Task<double?> WaitForLoadAsync(double? limit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (limit is not double max) return null;
        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool reported = false;
        while (true)
        {
            double? load = await _loadAverage(cancellationToken).ConfigureAwait(false);
            if (load is null)
            {
                _logger.Warn("The 15-minute load average is not available on this platform; running without the load gate.");
                return null;
            }
            if (load.Value < max) return max;
            if (waited.Elapsed >= timeout)
                throw new TimeoutException($"The 15-minute load average stayed at or above {max.ToString(CultureInfo.InvariantCulture)} for {timeout}.");
            if (!reported)
            {
                _logger.Info($"Waiting for the 15-minute load average ({load.Value.ToString("0.00", CultureInfo.InvariantCulture)}) to drop below {max.ToString(CultureInfo.InvariantCulture)}.");
                reported = true;
            }
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string RelativeProject(string repositoryRoot, string project)
    {
        string full = Path.IsPathRooted(project) ? Path.GetFullPath(project) : Path.GetFullPath(Path.Combine(repositoryRoot, project));
        string root = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
            throw new ArgumentException($"The benchmark project must be inside the repository {repositoryRoot}: {project}");
        string relative = full.Substring(root.Length);
        if (Directory.Exists(full))
        {
            string[] projects = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length != 1)
                throw new ArgumentException($"Expected one project file in {full}, found {projects.Length}.");
            relative = Path.Combine(relative, Path.GetFileName(projects[0]));
        }
        return relative;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
