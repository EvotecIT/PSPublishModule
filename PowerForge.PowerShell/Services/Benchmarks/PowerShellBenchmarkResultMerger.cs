namespace PowerForge;

internal static class PowerShellBenchmarkResultMerger
{
    internal static BenchmarkRunResult Merge(
        PowerShellBenchmarkSuite suite,
        IEnumerable<BenchmarkRunResult> results,
        DateTimeOffset started,
        PowerShellBenchmarkEnvironmentMetadata.SourceProvenance sourceProvenance)
    {
        BenchmarkRunResult[] childResults = results
            .Where(result => result is not null)
            .ToArray();
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var samples = childResults
            .SelectMany(result => result.Samples)
            .Select(sample => CopySample(sample, runId))
            .ToArray();
        var summary = new BenchmarkSummaryService().Summarize(samples, suite.OutlierMode);
        var result = new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples,
            Summary = summary,
            Comparison = PowerShellBenchmarkComparisonEvaluator.Build(suite, summary),
            Metadata = PowerShellBenchmarkEnvironmentMetadata.Build(suite, sourceProvenance),
            Environment = MergeEnvironment(childResults)
        };

        return result;
    }

    internal static BenchmarkEnvironmentInfo MergeEnvironment(
        IReadOnlyList<BenchmarkRunResult> results)
        => new()
        {
            OsFamily = MergeString(results, value => value.Environment.OsFamily),
            OsDescription = MergeString(results, value => value.Environment.OsDescription),
            OsArchitecture = MergeString(results, value => value.Environment.OsArchitecture),
            ProcessArchitecture = MergeString(results, value => value.Environment.ProcessArchitecture),
            ProcessorName = MergeString(results, value => value.Environment.ProcessorName),
            PhysicalProcessorCount = MergeNullableInt(results, value => value.Environment.PhysicalProcessorCount),
            PhysicalCoreCount = MergeNullableInt(results, value => value.Environment.PhysicalCoreCount),
            LogicalCoreCount = MergeNullableInt(results, value => value.Environment.LogicalCoreCount),
            RuntimeVersion = MergeRequiredString(results, value => value.Environment.RuntimeVersion),
            DotNetSdkVersion = MergeString(results, value => value.Environment.DotNetSdkVersion),
            Runner = MergeRequiredString(results, value => value.Environment.Runner),
            MachineName = MergeString(results, value => value.Environment.MachineName)
        };

    private static string MergeString(
        IReadOnlyList<BenchmarkRunResult> results,
        Func<BenchmarkRunResult, string> selector)
    {
        var values = results
            .Select(result => new
            {
                Host = HostLabel(result),
                Value = selector(result)?.Trim() ?? string.Empty
            })
            .Where(item => item.Value.Length > 0)
            .ToArray();
        string[] distinct = values
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length <= 1)
            return distinct.FirstOrDefault() ?? string.Empty;

        return string.Join(
            "; ",
            values
                .GroupBy(
                    item => item.Host + "\u001f" + item.Value,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Host, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .Select(item => $"{item.Host}={item.Value}"));
    }

    private static string MergeRequiredString(
        IReadOnlyList<BenchmarkRunResult> results,
        Func<BenchmarkRunResult, string> selector)
    {
        if (results.Count == 0 ||
            results.Any(result => string.IsNullOrWhiteSpace(selector(result))))
        {
            return string.Empty;
        }
        return MergeString(results, selector);
    }

    private static int? MergeNullableInt(
        IReadOnlyList<BenchmarkRunResult> results,
        Func<BenchmarkRunResult, int?> selector)
    {
        int?[] values = results
            .Select(selector)
            .Where(value => value.HasValue)
            .Distinct()
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static string HostLabel(BenchmarkRunResult result)
    {
        string[] hosts = result.Samples
            .Select(sample => sample.Host)
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return hosts.Length == 0 ? "Unknown" : string.Join("+", hosts);
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
}
