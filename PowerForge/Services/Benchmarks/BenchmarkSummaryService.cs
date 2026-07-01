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
            .GroupBy(s => MakeKey(s.Suite, s.Scenario, s.Operation, s.Engine, s.Host, s.Variables), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return BuildSummaryRow(first.Suite, first.Scenario, first.Operation, first.Engine, first.Host, first.Variables, group);
            })
            .OrderBy(r => r.Suite, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Operation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Engine, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
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
        foreach (var group in rows.GroupBy(r => MakeKey(r.Suite, r.Scenario, r.Operation, string.Empty, r.Host, r.Variables), StringComparer.OrdinalIgnoreCase))
        {
            var baseline = group.FirstOrDefault(r => string.Equals(r.Engine, baselineEngine, StringComparison.OrdinalIgnoreCase));
            var baselineValue = GetMetricValue(baseline, metric);
            foreach (var row in group.OrderBy(r => r.Engine, StringComparer.OrdinalIgnoreCase))
            {
                var actual = GetMetricValue(row, metric);
                result.Add(new BenchmarkComparisonRow
                {
                    Suite = row.Suite,
                    Scenario = row.Scenario,
                    Operation = row.Operation,
                    Host = row.Host,
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
        IReadOnlyDictionary<string, string?> variables,
        IEnumerable<BenchmarkSample> samples)
    {
        var all = samples.ToArray();
        var successful = all
            .Where(s => s.Status == BenchmarkSampleStatus.Succeeded)
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

        foreach (var metric in all
                     .Where(s => s.Status == BenchmarkSampleStatus.Succeeded)
                     .SelectMany(s => s.Metrics)
                     .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            row.Metrics[metric.Key] = metric.Average(k => k.Value);
        }

        return row;
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
        IReadOnlyDictionary<string, string?> variables)
        => string.Join("\u001f", suite ?? string.Empty, scenario ?? string.Empty, operation ?? string.Empty, engine ?? string.Empty, host ?? string.Empty, FormatVariables(variables));

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            "\u001e",
            (variables ?? new Dictionary<string, string?>())
                .Where(k => !IsBuiltInAxis(k.Key))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));

    private static bool IsBuiltInAxis(string key)
        => string.Equals(key, "Engine", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Operation", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string?> CopyVariables(IReadOnlyDictionary<string, string?> variables)
    {
        var copy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in variables)
            copy[entry.Key] = entry.Value;
        return copy;
    }
}
