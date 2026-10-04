namespace PowerForge;

/// <summary>Retains bounded accepted timing history and evaluates runner-specific regression limits.</summary>
public sealed partial class BenchmarkHistoryService
{
    private const int MaximumEntries = 500;

    /// <summary>Verifies a run without changing its history. Insufficient history never passes.</summary>
    /// <param name="history">Previously accepted runs, or null during initial calibration.</param>
    /// <param name="result">Current normalized run, including all raw samples.</param>
    /// <param name="request">Workload, runner identity and calibration policy.</param>
    /// <returns>Per-lane thresholds and verification state.</returns>
    public BenchmarkHistoryResult Evaluate(BenchmarkHistory? history, BenchmarkRunResult result, BenchmarkHistoryRequest request)
    {
        ValidateHistory(history);
        var current = CreateEntry(result, request);
        var comparable = (history?.Entries ?? Array.Empty<BenchmarkHistoryEntry>())
            .Where(entry => entry.WorkloadId == current.WorkloadId && entry.EnvironmentSha256 == current.EnvironmentSha256
                && entry.RunId != current.RunId && entry.FinishedUtc < current.FinishedUtc)
            .OrderByDescending(entry => entry.FinishedUtc).ThenBy(entry => entry.RunId, StringComparer.Ordinal)
            .Take(request.WindowSize).ToArray();
        var messages = new List<string>();
        var metrics = new List<BenchmarkHistoryMetricResult>();
        bool calibrating = false;
        var keys = current.Lanes.Keys.Concat(comparable.SelectMany(entry => entry.Lanes.Keys))
            .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal);
        foreach (string key in keys)
        {
            var values = comparable.Where(entry => entry.Lanes.TryGetValue(key, out var lane) && lane.SampleCount >= request.MinimumSamples)
                .Select(entry => entry.Lanes[key].MedianMs).OrderBy(value => value).ToArray();
            var metric = new BenchmarkHistoryMetricResult { Key = key, HistoryRuns = values.Length };
            metric.Label = current.Lanes.TryGetValue(key, out var currentLane) ? currentLane.Label : comparable.First(entry => entry.Lanes.ContainsKey(key)).Lanes[key].Label;
            if (currentLane is not null) metric.ActualMs = currentLane.MedianMs;
            else
            {
                metric.Regressed = true;
                messages.Add($"Historical benchmark lane '{metric.Label}' is missing from the current run.");
            }
            if (values.Length < request.MinimumRuns)
            {
                calibrating = true;
                messages.Add($"Benchmark lane '{metric.Label}' has {values.Length} accepted runs; {request.MinimumRuns} are required.");
            }
            else
            {
                double baseline = Median(values);
                double noise = Median(values.Select(value => Math.Abs(value - baseline)).OrderBy(value => value).ToArray());
                double allowance = Math.Max(request.AbsoluteToleranceMs, Math.Max(baseline * request.RelativeTolerance, 3 * noise));
                double allowed = baseline + allowance;
                if (!Finite(allowed)) throw new InvalidOperationException("History allowance exceeds finite duration bounds.");
                metric.BaselineMs = baseline; metric.NoiseMs = noise; metric.AllowedMs = allowed;
                if (metric.ActualMs > allowed)
                {
                    metric.Regressed = true;
                    messages.Add($"Benchmark lane '{metric.Label}' exceeds its runner-specific duration limit.");
                }
            }
            metrics.Add(metric);
        }
        return new BenchmarkHistoryResult
        {
            Passed = !calibrating && !metrics.Any(metric => metric.Regressed), Calibrating = calibrating,
            EnvironmentSha256 = current.EnvironmentSha256, Metrics = metrics.ToArray(), Messages = messages.ToArray()
        };
    }

    /// <summary>Explicitly accepts a run for calibration. Failed or incomplete measurements are rejected.</summary>
    /// <param name="history">Previously accepted runs, or null.</param>
    /// <param name="result">Normalized measured result to accept.</param>
    /// <param name="request">Workload and runner calibration policy.</param>
    /// <returns>New bounded history. Existing input objects are not mutated.</returns>
    public BenchmarkHistory Record(BenchmarkHistory? history, BenchmarkRunResult result, BenchmarkHistoryRequest request)
    {
        ValidateHistory(history);
        var entry = CreateEntry(result, request);
        var entries = history?.Entries ?? Array.Empty<BenchmarkHistoryEntry>();
        var existing = entries.FirstOrDefault(item => item.RunId == entry.RunId && item.WorkloadId == entry.WorkloadId);
        if (existing is not null)
        {
            if (existing.EnvironmentSha256 != entry.EnvironmentSha256 || existing.ResultSha256 != entry.ResultSha256)
                throw new InvalidOperationException("A history run identifier cannot be reused for different measured content.");
            return new BenchmarkHistory { Entries = entries.ToArray() };
        }
        return new BenchmarkHistory
        {
            Entries = entries.Append(entry).OrderByDescending(item => item.FinishedUtc)
                .ThenBy(item => item.RunId, StringComparer.Ordinal).Take(MaximumEntries).ToArray()
        };
    }

    /// <summary>Reads history and verifies a run, or atomically records an explicitly accepted run.</summary>
    /// <param name="historyPath">Local history JSON file.</param>
    /// <param name="result">Normalized measured result.</param>
    /// <param name="request">Workload and calibration policy.</param>
    /// <param name="update">Accept the run instead of verifying; never enable automatically after a failed gate.</param>
    /// <returns>Verification state, or Updated=true without a verification claim.</returns>
    public BenchmarkHistoryResult EvaluateFile(string historyPath, BenchmarkRunResult result, BenchmarkHistoryRequest request, bool update = false)
    {
        if (string.IsNullOrWhiteSpace(historyPath)) throw new ArgumentException("History path is required.", nameof(historyPath));
        string path = BenchmarkJson.ResolveWritePath(historyPath);
        using var lease = update ? BenchmarkFileUpdateLock.Acquire(path) : null;
        if (File.Exists(path) && new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidOperationException("Timing history exceeds the 32 MiB file limit.");
        BenchmarkHistory? history = File.Exists(path) ? BenchmarkJson.Read<BenchmarkHistory>(path) : null;
        if (!update) return Evaluate(history, result, request);
        var recorded = Record(history, result, request);
        var payload = BenchmarkJson.SerializeCanonicalBytes(recorded);
        if (payload.Length > 32 * 1024 * 1024)
            throw new InvalidOperationException("Timing history exceeds the 32 MiB file limit.");
        BenchmarkJson.WriteBytes(path, payload);
        return new BenchmarkHistoryResult { Updated = true, EnvironmentSha256 = CreateEntry(result, request).EnvironmentSha256 };
    }

    private static double Median(double[] values)
        => values.Length % 2 == 1 ? values[values.Length / 2] : values[values.Length / 2 - 1] / 2 + values[values.Length / 2] / 2;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
