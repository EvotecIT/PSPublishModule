namespace PowerForge;

public sealed partial class BenchmarkHistoryService
{
    private static readonly string[] PolicyKeys = { "warmupCount", "iterationCount", "runOrder", "outlierMode", "memoryCleanup",
        "processAffinityMask", "processPriority", "benchmark.PowerPlan" };

    private static BenchmarkHistoryEntry CreateEntry(BenchmarkRunResult result, BenchmarkHistoryRequest request)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkloadId) || string.IsNullOrWhiteSpace(request.RunnerIdentity))
            throw new ArgumentException("Workload and stable runner identities are required.", nameof(request));
        if (request.MinimumRuns < 3 || request.MinimumRuns > 100 || request.WindowSize < request.MinimumRuns || request.WindowSize > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "History requires 3..100 runs and a window at least that large.");
        if (request.MinimumSamples < 3 || request.MinimumSamples > 10000 || !Finite(request.RelativeTolerance)
            || request.RelativeTolerance < 0 || !Finite(request.AbsoluteToleranceMs) || request.AbsoluteToleranceMs < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Sample counts and duration allowances must be finite and nonnegative.");
        if (string.IsNullOrWhiteSpace(result.RunId) || result.FinishedUtc == default || result.FinishedUtc < result.StartedUtc)
            throw new InvalidOperationException("History requires a completed independent run identifier and timestamps.");
        if (result.Samples is null || result.Samples.Length == 0 || result.Samples.Any(sample => sample is null
            || sample.RunId != result.RunId || sample.Suite != result.Suite
            || sample.Status != BenchmarkSampleStatus.Succeeded || !Finite(sample.DurationMs) || sample.DurationMs <= 0))
            throw new InvalidOperationException("History requires successful finite positive raw duration samples; failed or skipped runs are not accepted.");
        var environment = result.Environment ?? throw new InvalidOperationException("Benchmark environment is required.");
        string[] identity = { request.RunnerIdentity.Trim(), environment.OsFamily, environment.OsArchitecture,
            environment.ProcessArchitecture, environment.ProcessorName, environment.RuntimeVersion, environment.Runner };
        if (identity.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Runner environment requires OS, architecture, CPU, runtime and runner version identity.");
        var policy = PolicyKeys.ToDictionary(key => key, key => Metadata(result, key), StringComparer.Ordinal);
        if (!int.TryParse(policy["iterationCount"], out int iterations) || iterations < request.MinimumSamples
            || string.IsNullOrWhiteSpace(policy["outlierMode"]))
            throw new InvalidOperationException("History requires measured iteration and outlier policies.");
        var lanes = new Dictionary<string, BenchmarkHistoryLane>(StringComparer.Ordinal);
        foreach (var group in result.Samples.GroupBy(sample => LaneKey(sample.Suite, sample.Scenario, sample.Operation,
                     sample.Engine, sample.Host, sample.Os, sample.RunMode, sample.Variables)))
        {
            var samples = group.ToArray();
            if (samples.Length != iterations || samples.Select(sample => sample.Iteration).Distinct().Count() != iterations)
                throw new InvalidOperationException("Every history lane must contain all independent measured iterations.");
            var first = samples[0];
            // Duration history uses raw timings, never supplied summaries or custom metric overrides.
            lanes.Add(group.Key, new BenchmarkHistoryLane { Label = $"{first.Scenario}/{first.Operation}/{first.Engine}",
                MedianMs = Median(samples.Select(sample => sample.DurationMs).OrderBy(value => value).ToArray()), SampleCount = iterations });
        }
        if (lanes.Count > 512) throw new InvalidOperationException("Timing history supports at most 512 lanes per run.");
        return new BenchmarkHistoryEntry
        {
            RunId = result.RunId, FinishedUtc = result.FinishedUtc, WorkloadId = request.WorkloadId.Trim(),
            EnvironmentSha256 = BenchmarkJson.ComputeSha256(new { Identity = identity, environment.DotNetSdkVersion,
                environment.OsDescription, environment.LogicalCoreCount, environment.PhysicalCoreCount, Policy = policy }),
            ResultSha256 = BenchmarkJson.ComputeSha256(result), SourceCommit = Metadata(result, "benchmark.SourceCandidateCommit"),
            Lanes = lanes
        };
    }

    private static string LaneKey(string suite, string scenario, string operation, string engine, string host, string os,
        string runMode, IReadOnlyDictionary<string, string?> variables)
        => BenchmarkJson.ComputeSha256(new { suite, scenario, operation, engine, host, os, runMode,
            Variables = variables.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray() });

    private static string Metadata(BenchmarkRunResult result, string key)
        => result.Metadata?.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static void ValidateHistory(BenchmarkHistory? history)
    {
        if (history is null) return;
        if (history.SchemaVersion != 1 || history.Entries is null || history.Entries.Length > MaximumEntries)
            throw new InvalidOperationException("Unsupported or excessive benchmark history.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in history.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RunId) || string.IsNullOrWhiteSpace(entry.WorkloadId)
                || string.IsNullOrWhiteSpace(entry.EnvironmentSha256) || string.IsNullOrWhiteSpace(entry.ResultSha256)
                || entry.FinishedUtc == default || entry.Lanes is null || entry.Lanes.Count == 0 || entry.Lanes.Count > 512
                || entry.Lanes.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value is null
                    || !Finite(item.Value.MedianMs) || item.Value.MedianMs <= 0 || item.Value.SampleCount < 3)
                || !ids.Add(BenchmarkJson.ComputeSha256(new { entry.WorkloadId, entry.RunId })))
                throw new InvalidOperationException("History contains invalid or duplicate accepted runs.");
        }
    }
}
