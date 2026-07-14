namespace PowerForge;

internal static class PowerShellBenchmarkResultMerger
{
    internal static BenchmarkRunResult Merge(PowerShellBenchmarkSuite suite, IEnumerable<BenchmarkRunResult> results, DateTimeOffset started)
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var samples = results
            .Where(result => result is not null)
            .SelectMany(result => result.Samples)
            .Select(sample => CopySample(sample, runId))
            .ToArray();
        var summarizer = new BenchmarkSummaryService();
        var summary = summarizer.Summarize(samples, suite.OutlierMode);
        var result = new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples,
            Summary = summary,
            Comparison = suite.Comparisons
                .Where(comparison => !string.IsNullOrWhiteSpace(comparison.Baseline))
                .SelectMany(comparison => GetComparisonMetrics(comparison).SelectMany(metric => summarizer.Compare(summary, comparison.Baseline, metric, comparison.TieTolerance)))
                .ToArray(),
            Metadata = PowerShellBenchmarkEnvironmentMetadata.Build(suite)
        };

        return result;
    }

    private static BenchmarkSample CopySample(BenchmarkSample sample, string runId)
        => new()
        {
            RunId = runId,
            Suite = sample.Suite,
            Scenario = sample.Scenario,
            Operation = sample.Operation,
            Engine = sample.Engine,
            Host = sample.Host,
            Os = sample.Os,
            RunMode = sample.RunMode,
            Iteration = sample.Iteration,
            Status = sample.Status,
            DurationMs = sample.DurationMs,
            AllocatedBytes = sample.AllocatedBytes,
            WorkingSetDeltaBytes = sample.WorkingSetDeltaBytes,
            OutputMetric = sample.OutputMetric,
            Reason = sample.Reason,
            Variables = new Dictionary<string, string?>(sample.Variables, StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(sample.Metrics, StringComparer.OrdinalIgnoreCase)
        };

    private static IEnumerable<string> GetComparisonMetrics(PowerShellBenchmarkComparison comparison)
        => comparison.Metrics is null || comparison.Metrics.Length == 0
            ? new[] { "MedianMs" }
            : comparison.Metrics;
}
