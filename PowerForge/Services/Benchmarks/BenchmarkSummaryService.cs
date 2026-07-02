namespace PowerForge;

/// <summary>
/// Aggregates raw benchmark samples into summary and comparison rows.
/// </summary>
public sealed class BenchmarkSummaryService
{
    /// <summary>
    /// Creates summary rows from raw samples.
    /// </summary>
    /// <param name="samples">Raw benchmark samples.</param>
    /// <returns>Summary rows.</returns>
    public BenchmarkSummaryRow[] Summarize(IEnumerable<BenchmarkSample> samples)
    {
        return (samples ?? Array.Empty<BenchmarkSample>())
            .GroupBy(s => MakeKey(s.Suite, s.Scenario, s.Operation, s.Engine, s.Host, s.Os, s.Variables), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return BuildSummaryRow(first.Suite, first.Scenario, first.Operation, first.Engine, first.Host, first.Os, first.Variables, group);
            })
            .OrderBy(r => r.Suite, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Operation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Engine, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Os, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => FormatVariables(r.Variables), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Creates comparison rows against a baseline engine.
    /// </summary>
    /// <param name="summary">Summary rows.</param>
    /// <param name="baselineEngine">Baseline engine name.</param>
    /// <param name="metric">Metric name to compare.</param>
    /// <returns>Comparison rows.</returns>
    public BenchmarkComparisonRow[] Compare(IEnumerable<BenchmarkSummaryRow> summary, string baselineEngine, string metric = "MedianMs")
    {
        var rows = (summary ?? Array.Empty<BenchmarkSummaryRow>()).ToArray();
        var result = new List<BenchmarkComparisonRow>();
        foreach (var group in rows.GroupBy(r => MakeKey(r.Suite, r.Scenario, r.Operation, string.Empty, r.Host, r.Os, r.Variables), StringComparer.Ordinal))
        {
            if (group.All(r => string.Equals(r.Status, "Skipped", StringComparison.OrdinalIgnoreCase)))
                continue;

            var baseline = ResolveBaseline(group, baselineEngine);
            if (baseline is null)
                throw new InvalidOperationException($"Benchmark comparison baseline '{baselineEngine}' was not found for {DescribeGroup(group.First())}.");
            var baselineValue = GetMetricValue(baseline, metric);
            if (!baselineValue.HasValue)
                throw new InvalidOperationException($"Benchmark comparison baseline '{baselineEngine}' has no value for metric '{metric}' in {DescribeGroup(baseline)}.");
            foreach (var row in group.OrderBy(r => r.Engine, StringComparer.OrdinalIgnoreCase))
            {
                var actual = GetMetricValue(row, metric);
                result.Add(new BenchmarkComparisonRow
                {
                    Suite = row.Suite,
                    Scenario = row.Scenario,
                    Operation = row.Operation,
                    Host = row.Host,
                    Os = row.Os,
                    Variables = CopyVariables(row.Variables),
                    Engine = row.Engine,
                    BaselineEngine = baselineEngine,
                    Metric = metric,
                    Actual = actual,
                    Baseline = baselineValue,
                    Ratio = actual.HasValue && baselineValue.HasValue && Math.Abs(baselineValue.Value) > double.Epsilon
                        ? actual.Value / baselineValue.Value
                        : null
                });
            }
        }

        return result.ToArray();
    }

    private static BenchmarkSummaryRow? ResolveBaseline(IEnumerable<BenchmarkSummaryRow> group, string baselineEngine)
    {
        var rows = group.ToArray();
        var exact = rows.Where(r => string.Equals(r.Engine, baselineEngine, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1)
            return exact[0];
        if (exact.Length > 1)
            throw new InvalidOperationException($"Benchmark comparison baseline '{baselineEngine}' matched multiple exact engine rows.");

        var insensitive = rows.Where(r => string.Equals(r.Engine, baselineEngine, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (insensitive.Length == 0)
            return null;

        var distinctEngineNames = insensitive
            .Select(r => r.Engine)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctEngineNames.Length > 1)
            throw new InvalidOperationException($"Benchmark comparison baseline '{baselineEngine}' is ambiguous because matching engine rows differ only by case: {string.Join(", ", distinctEngineNames)}.");

        return insensitive[0];
    }

    internal static double? GetMetricValue(BenchmarkSummaryRow? row, string metric)
    {
        if (row is null) return null;
        var name = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric.Trim();
        if (string.Equals(name, "MedianMs", StringComparison.OrdinalIgnoreCase)) return row.MedianMs;
        if (string.Equals(name, "MeanMs", StringComparison.OrdinalIgnoreCase)) return row.MeanMs;
        if (string.Equals(name, "MinMs", StringComparison.OrdinalIgnoreCase)) return row.MinMs;
        if (string.Equals(name, "MaxMs", StringComparison.OrdinalIgnoreCase)) return row.MaxMs;
        if (row.Metrics.TryGetValue(name, out var value)) return value;
        foreach (var entry in row.Metrics)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static BenchmarkSummaryRow BuildSummaryRow(
        string suite,
        string scenario,
        string operation,
        string engine,
        string host,
        string os,
        IReadOnlyDictionary<string, string?> variables,
        IEnumerable<BenchmarkSample> samples)
    {
        var all = samples.ToArray();
        var successfulSamples = all
            .Where(s => s.Status == BenchmarkSampleStatus.Succeeded)
            .ToArray();
        var successful = successfulSamples
            .Select(s => s.DurationMs)
            .OrderBy(v => v)
            .ToArray();

        var row = new BenchmarkSummaryRow
        {
            Suite = suite,
            Scenario = scenario,
            Operation = operation,
            Engine = engine,
            Host = host,
            Os = os,
            Variables = CopyVariables(variables),
            SampleCount = successful.Length,
            FailureCount = all.Count(s => s.Status == BenchmarkSampleStatus.Failed),
            Status = all.Any(s => s.Status == BenchmarkSampleStatus.Failed)
                ? "Failed"
                : successful.Length > 0
                    ? "Succeeded"
                    : all.Any(s => s.Status == BenchmarkSampleStatus.Skipped)
                        ? "Skipped"
                        : "Failed",
            MedianMs = Median(successful),
            MeanMs = successful.Length == 0 ? null : successful.Average(),
            MinMs = successful.Length == 0 ? null : successful.Min(),
            MaxMs = successful.Length == 0 ? null : successful.Max()
        };

        foreach (var metric in successfulSamples
                     .SelectMany(s => s.Metrics)
                     .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (metric.Count() != successfulSamples.Length)
                continue;
            row.Metrics[metric.Key] = metric.Average(k => k.Value);
        }

        row.MedianMs = GetPrimaryTimingOverride(row.Metrics, "MedianMs") ?? row.MedianMs;
        row.MeanMs = GetPrimaryTimingOverride(row.Metrics, "MeanMs") ?? row.MeanMs;
        row.MinMs = GetPrimaryTimingOverride(row.Metrics, "MinMs") ?? row.MinMs;
        row.MaxMs = GetPrimaryTimingOverride(row.Metrics, "MaxMs") ?? row.MaxMs;
        return row;
    }

    private static double? GetPrimaryTimingOverride(IReadOnlyDictionary<string, double> metrics, string name)
    {
        if (metrics.TryGetValue(name, out var value))
            return value;
        foreach (var entry in metrics)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        var middle = values.Count / 2;
        if (values.Count % 2 == 1) return values[middle];
        return (values[middle - 1] + values[middle]) / 2.0;
    }

    private static string MakeKey(
        string? suite,
        string? scenario,
        string? operation,
        string? engine,
        string? host,
        string? os,
        IReadOnlyDictionary<string, string?> variables)
        => string.Join("\u001f", suite ?? string.Empty, scenario ?? string.Empty, operation ?? string.Empty, engine ?? string.Empty, host ?? string.Empty, os ?? string.Empty, FormatVariables(variables));

    private static string DescribeGroup(BenchmarkSummaryRow row)
        => $"suite '{row.Suite}', scenario '{row.Scenario}', operation '{row.Operation}', host '{row.Host}', os '{row.Os}', variables '{FormatVariables(row.Variables)}'";

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            "\u001e",
            (variables ?? new Dictionary<string, string?>())
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));

    private static Dictionary<string, string?> CopyVariables(IReadOnlyDictionary<string, string?> variables)
    {
        var copy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in variables)
            copy[entry.Key] = entry.Value;
        return copy;
    }
}
