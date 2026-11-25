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
    private readonly bool _loadSupported;
    private bool _loadUnsupportedReported;

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
        _loadSupported = loadAverage is not null || SystemLoadAverage.IsSupported;
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

        var (baselineRevision, baselineRef) = string.IsNullOrWhiteSpace(spec.Baseline)
            ? await DefaultBranchAsync(repositoryRoot, cancellationToken).ConfigureAwait(false)
            : (spec.Baseline!.Trim(), spec.Baseline!.Trim());
        var baseline = new BenchmarkRevisionSide
        {
            Name = BenchmarkRevisionComparison.BaselineName,
            Revision = baselineRevision,
            Commit = await ResolveCommitAsync(repositoryRoot, baselineRef, cancellationToken).ConfigureAwait(false)
        };
        bool candidateIsWorkingTree = string.IsNullOrWhiteSpace(spec.Candidate);
        var candidate = new BenchmarkRevisionSide
        {
            Name = BenchmarkRevisionComparison.CandidateName,
            Revision = candidateIsWorkingTree ? "working tree" : spec.Candidate!.Trim(),
            Commit = candidateIsWorkingTree
                ? string.Empty
                : await ResolveCommitAsync(repositoryRoot, spec.Candidate!.Trim(), cancellationToken).ConfigureAwait(false)
        };

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
            project = ResolveProjectFile(project, roots[baseline.Name], roots[candidate.Name]);

            // Wait for a quiet machine before taking the lock too, so a busy machine never keeps others waiting on it.
            await WaitForLoadAsync(spec.MaxLoadAverage, spec.LoadWaitTimeout, cancellationToken).ConfigureAwait(false);
            using var benchmarkLock = string.IsNullOrWhiteSpace(spec.LockPath)
                ? null
                : await AcquireLockAsync(spec, baseline, candidate, cancellationToken).ConfigureAwait(false);
            if (benchmarkLock is not null)
                _logger.Info($"Holding benchmark lock {benchmarkLock.Path}.");

            if (candidateIsWorkingTree)
            {
                // Read right before building, so the report describes the source that is built.
                candidate.Commit = await ResolveCommitAsync(repositoryRoot, "HEAD", cancellationToken).ConfigureAwait(false);
                candidate.Dirty = (await GitAsync(repositoryRoot, cancellationToken,
                    await StatusArgumentsAsync(repositoryRoot, new[] { workRoot, spec.LockPath }, cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false)).Length > 0;
            }

            foreach (var side in new[] { baseline.Name, candidate.Name })
                await BuildAsync(spec, roots[side], project, Path.Combine(workRoot, "logs", side + "-build.log"), side, cancellationToken).ConfigureAwait(false);

            var results = new Dictionary<string, List<BenchmarkRunResult?>>(StringComparer.Ordinal)
            {
                [baseline.Name] = new(),
                [candidate.Name] = new()
            };
            var order = new List<string>();
            BenchmarkEnvironmentInfo? environment = null;
            BenchmarkRunnerInfo? reportedRunner = null;
            for (int round = 1; round <= spec.Rounds; round++)
            {
                foreach (string side in BenchmarkRevisionComparison.RoundOrder(round))
                {
                    await WaitForLoadAsync(spec.MaxLoadAverage, spec.LoadWaitTimeout, cancellationToken).ConfigureAwait(false);
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

            if (results.Values.All(rounds => rounds.All(result => result is null)))
                throw new InvalidOperationException(
                    "No run produced a BenchmarkDotNet report: the filter matched nothing or every run failed. Rerun with --keep-artifacts to read the logs.");

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
                    InProcess = BenchmarkRevisionComparison.RunsInProcess(benchmarkArguments)
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
        return await BenchmarkLockDirectory.AcquireAsync(spec.LockPath!, description, spec.LockTimeout, _pollInterval,
            owner => _logger.Info($"Waiting for benchmark lock {spec.LockPath}: {owner}"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the 15-minute load average is below the limit. On a platform without a load average the gate is
    /// skipped; a reading that fails on a supported platform counts as busy and is retried.
    /// </summary>
    private async Task WaitForLoadAsync(double? limit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (limit is not double max) return;
        if (!_loadSupported)
        {
            if (!_loadUnsupportedReported)
                _logger.Warn("This platform has no 15-minute load average; running without the load gate.");
            _loadUnsupportedReported = true;
            return;
        }

        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool reported = false;
        while (true)
        {
            double? load = await _loadAverage(cancellationToken).ConfigureAwait(false);
            if (load is double value && value < max) return;
            if (waited.Elapsed >= timeout)
                throw new TimeoutException(load is null
                    ? $"The 15-minute load average could not be read for {timeout}."
                    : $"The 15-minute load average stayed at or above {max.ToString(CultureInfo.InvariantCulture)} for {timeout}.");
            if (!reported)
            {
                _logger.Info(load is double busy
                    ? $"Waiting for the 15-minute load average ({busy.ToString("0.00", CultureInfo.InvariantCulture)}) to drop below {max.ToString(CultureInfo.InvariantCulture)}."
                    : "Could not read the 15-minute load average; retrying.");
                reported = true;
            }
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the project path relative to the repository root without looking at the current checkout: a folder is
    /// resolved later against the revisions being compared.
    /// </summary>
    private static string RelativeProject(string repositoryRoot, string project)
    {
        string full = Path.IsPathRooted(project) ? Path.GetFullPath(project) : Path.GetFullPath(Path.Combine(repositoryRoot, project));
        string root = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), comparison))
            return ".";
        if (!full.StartsWith(root, comparison))
            throw new ArgumentException($"The benchmark project must be inside the repository {repositoryRoot}: {project}");
        return full.Substring(root.Length);
    }

    /// <summary>
    /// Resolves a project folder to its single project file in the baseline revision and checks that both revisions have it.
    /// </summary>
    private static string ResolveProjectFile(string project, string baselineRoot, string candidateRoot)
    {
        string relative = project;
        if (!Path.GetExtension(project).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
        {
            string folder = Path.Combine(baselineRoot, project);
            string[] projects = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.*proj", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (projects.Length != 1)
                throw new ArgumentException($"Expected one project file in {project} of the baseline revision, found {projects.Length}.");
            relative = project == "." ? Path.GetFileName(projects[0]) : Path.Combine(project, Path.GetFileName(projects[0]));
        }

        foreach (var (name, root) in new[] { (BenchmarkRevisionComparison.BaselineName, baselineRoot), (BenchmarkRevisionComparison.CandidateName, candidateRoot) })
        {
            if (!File.Exists(Path.Combine(root, relative)))
                throw new FileNotFoundException($"The {name} revision has no benchmark project at {relative}.", Path.Combine(root, relative));
        }
        return relative;
    }

    /// <summary>
    /// Status arguments that list tracked changes and untracked files, leaving out the comparison's own scratch
    /// folder and lock when they sit inside the repository. Git computes their repository-relative paths, so a path
    /// reached through a symbolic link (macOS temp folders, for example) is still recognised.
    /// </summary>
    private async Task<string[]> StatusArgumentsAsync(string repositoryRoot, IEnumerable<string?> ownedPaths, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "status", "--porcelain", "--untracked-files=normal", "--", "." };
        foreach (string? owned in ownedPaths)
        {
            if (string.IsNullOrWhiteSpace(owned) || !Directory.Exists(owned)) continue;
            var location = await _processRunner.RunAsync(
                new ProcessRunRequest("git", Path.GetFullPath(owned!), new[] { "rev-parse", "--show-toplevel", "--show-prefix" }, TimeSpan.FromMinutes(1)),
                cancellationToken).ConfigureAwait(false);
            string[] lines = location.StdOut.Replace("\r", string.Empty).Split('\n');
            if (location.ExitCode != 0 || lines.Length < 2 || lines[1].Trim().Length == 0 ||
                !string.Equals(Path.GetFullPath(lines[0].Trim()), repositoryRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            arguments.Add(":(top,exclude)" + lines[1].Trim());
        }
        return arguments.ToArray();
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
