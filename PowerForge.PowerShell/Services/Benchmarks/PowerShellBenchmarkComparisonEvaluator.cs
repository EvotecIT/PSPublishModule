using System.Globalization;

namespace PowerForge;

internal static class PowerShellBenchmarkComparisonEvaluator
{
    internal static BenchmarkComparisonRow[] Build(
        PowerShellBenchmarkSuite suite,
        IReadOnlyList<BenchmarkSummaryRow> summary)
    {
        var summarizer = new BenchmarkSummaryService();
        return suite.Comparisons
            .Where(comparison => !string.IsNullOrWhiteSpace(comparison.Baseline))
            .SelectMany(comparison => GetMetrics(comparison)
                .SelectMany(metric => summarizer.Compare(summary, comparison.Baseline, metric, comparison.TieTolerance)))
            .ToArray();
    }

    internal static void ValidateGates(
        PowerShellBenchmarkSuite suite,
        IReadOnlyList<BenchmarkSummaryRow> summary)
    {
        var summarizer = new BenchmarkSummaryService();
        foreach (var comparison in suite.Comparisons.Where(static comparison => comparison.RequireBaselineFastest))
        {
            foreach (var metric in GetMetrics(comparison))
            {
                var rows = summarizer.Compare(summary, comparison.Baseline, metric, comparison.TieTolerance);
                ValidateBaselineFastest(comparison, metric, rows);
            }
        }
    }

    private static void ValidateBaselineFastest(
        PowerShellBenchmarkComparison comparison,
        string metric,
        IReadOnlyList<BenchmarkComparisonRow> rows)
    {
        if (!BenchmarkComparisonSemantics.IsDurationMetric(metric))
            throw new NotSupportedException($"Benchmark comparison metric '{metric}' cannot require the baseline to be fastest because only duration metrics have lower-is-better semantics.");

        var failures = new List<string>();
        foreach (var group in rows.GroupBy(GroupKey, StringComparer.Ordinal))
        {
            var competitors = group
                .Where(row => !string.Equals(row.Engine, comparison.Baseline, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (competitors.Length == 0)
                continue;

            var failedCompetitors = competitors
                .Where(row => IsFailed(row)
                              || (!row.Actual.HasValue && !IsSkipped(row)))
                .Select(row => row.Engine)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (failedCompetitors.Length > 0)
            {
                failures.Add($"{Describe(group.First())}: competitor {string.Join(", ", failedCompetitors)} failed");
                continue;
            }

            var successfulCompetitors = competitors
                .Where(static row => row.Actual.HasValue && !IsFailed(row))
                .ToArray();
            if (successfulCompetitors.Length == 0)
                continue;

            var baseline = group.FirstOrDefault(row =>
                string.Equals(row.Engine, comparison.Baseline, StringComparison.OrdinalIgnoreCase));
            if (baseline?.Actual is null || IsFailed(baseline))
            {
                failures.Add($"{Describe(group.First())}: baseline {comparison.Baseline} failed");
                continue;
            }

            var fastest = successfulCompetitors.OrderBy(static row => row.Actual!.Value).First();
            var tolerance = Math.Max(0, comparison.TieTolerance);
            if (baseline.Actual.Value <= fastest.Actual!.Value * (1 + tolerance))
                continue;

            failures.Add(
                $"{Describe(group.First())}: {comparison.Baseline} {Format(baseline.Actual.Value)} is slower than {fastest.Engine} {Format(fastest.Actual.Value)} beyond the {tolerance:P0} tie tolerance");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"Benchmark comparison gate requires baseline '{comparison.Baseline}' to be fastest or tied for metric '{metric}', but {failures.Count} lane(s) failed. {string.Join("; ", failures)}");
    }

    private static IEnumerable<string> GetMetrics(PowerShellBenchmarkComparison comparison)
        => comparison.Metrics is null || comparison.Metrics.Length == 0
            ? new[] { "MedianMs" }
            : comparison.Metrics;

    private static bool IsFailed(BenchmarkComparisonRow row)
        => string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkipped(BenchmarkComparisonRow row)
        => string.Equals(row.Status, "Skipped", StringComparison.OrdinalIgnoreCase);

    private static string GroupKey(BenchmarkComparisonRow row)
        => string.Join(
            "\u001f",
            row.Suite,
            row.Scenario,
            row.Operation,
            row.Host,
            row.Os,
            row.RunMode,
            row.Metric,
            string.Join(
                "\u001e",
                row.Variables
                    .OrderBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(variable => variable.Key + "=" + variable.Value)));

    private static string Describe(BenchmarkComparisonRow row)
    {
        var variables = row.Variables.Count == 0
            ? string.Empty
            : ", variables " + string.Join(
                ", ",
                row.Variables
                    .OrderBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(variable => variable.Key + "=" + variable.Value));
        return $"scenario '{row.Scenario}', operation '{row.Operation}', host '{row.Host}', OS '{row.Os}'{variables}";
    }

    private static string Format(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
}
