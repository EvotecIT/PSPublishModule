using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Writes PowerShell benchmark artifacts and document blocks.
/// </summary>
internal static class PowerShellBenchmarkArtifactWriter
{
    /// <summary>
    /// Writes all requested artifacts for a benchmark run.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <param name="result">Run result.</param>
    public static void WriteArtifacts(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
    {
        var outputRoot = Path.Combine(suite.OutputRoot, result.RunId);
        Directory.CreateDirectory(outputRoot);
        if (suite.Artifacts.HasFlag(BenchmarkArtifactKind.Json))
        {
            var samplesPath = Path.Combine(outputRoot, "samples.json");
            var summaryPath = Path.Combine(outputRoot, "summary.json");
            var comparisonPath = Path.Combine(outputRoot, "comparison.json");
            var metadataPath = Path.Combine(outputRoot, "metadata.json");
            var runReportPath = Path.Combine(outputRoot, "run-report.json");
            result.Artifacts["samples.json"] = samplesPath;
            result.Artifacts["summary.json"] = summaryPath;
            result.Artifacts["comparison.json"] = comparisonPath;
            result.Artifacts["metadata.json"] = metadataPath;
            result.Artifacts["run-report.json"] = runReportPath;
            BenchmarkJson.Write(samplesPath, result.Samples);
            BenchmarkJson.Write(summaryPath, result.Summary);
            BenchmarkJson.Write(comparisonPath, result.Comparison);
            BenchmarkJson.Write(metadataPath, result.Metadata);
        }

        if (suite.Artifacts.HasFlag(BenchmarkArtifactKind.Csv))
        {
            var samplesCsv = Path.Combine(outputRoot, "samples.csv");
            var summaryCsv = Path.Combine(outputRoot, "summary.csv");
            File.WriteAllText(samplesCsv, WriteSamplesCsv(result.Samples), new UTF8Encoding(false));
            File.WriteAllText(summaryCsv, WriteSummaryCsv(result.Summary), new UTF8Encoding(false));
            result.Artifacts["samples.csv"] = samplesCsv;
            result.Artifacts["summary.csv"] = summaryCsv;
        }

        if (suite.Artifacts.HasFlag(BenchmarkArtifactKind.Markdown))
        {
            var renderer = new BenchmarkMarkdownRenderer();
            var summaryMd = Path.Combine(outputRoot, "summary.md");
            var comparisonMd = Path.Combine(outputRoot, "comparison.md");
            File.WriteAllText(summaryMd, renderer.RenderSummaryTable(result.Summary), new UTF8Encoding(false));
            File.WriteAllText(comparisonMd, renderer.RenderComparisonTable(result.Comparison), new UTF8Encoding(false));
            result.Artifacts["summary.md"] = summaryMd;
            result.Artifacts["comparison.md"] = comparisonMd;
        }

        if (suite.Artifacts.HasFlag(BenchmarkArtifactKind.Json) && result.Artifacts.TryGetValue("run-report.json", out var runReportFinalPath))
            BenchmarkJson.Write(runReportFinalPath, result);
    }

    /// <summary>
    /// Updates declared Markdown benchmark blocks.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <param name="result">Run result.</param>
    public static void UpdateReadmeBlocks(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
    {
        if (result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Failed))
        {
            return;
        }

        var updater = new BenchmarkDocumentUpdater();
        var renderer = new BenchmarkMarkdownRenderer();
        foreach (var block in suite.ReadmeBlocks)
        {
            var markdown = block.Renderer switch
            {
                var value when string.Equals(value, "SummaryTable", StringComparison.OrdinalIgnoreCase) => renderer.RenderSummaryTable(result.Summary),
                var value when string.Equals(value, "ComparisonTable", StringComparison.OrdinalIgnoreCase) => renderer.RenderComparisonTable(result.Comparison),
                _ => throw new NotSupportedException($"Benchmark README renderer '{block.Renderer}' is not supported.")
            };
            updater.UpdateBlock(block.Path, block.BlockId, markdown);
        }
    }

    /// <summary>
    /// Validates declared Markdown benchmark blocks before benchmark work starts.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    public static void ValidateReadmeBlocks(PowerShellBenchmarkSuite suite)
    {
        var updater = new BenchmarkDocumentUpdater();
        foreach (var block in suite.ReadmeBlocks)
        {
            if (IsSupportedReadmeRenderer(block.Renderer))
            {
                updater.ValidateBlock(block.Path, block.BlockId);
                continue;
            }

            throw new NotSupportedException($"Benchmark README renderer '{block.Renderer}' is not supported.");
        }
    }

    /// <summary>
    /// Determines whether a Markdown renderer name is supported.
    /// </summary>
    /// <param name="renderer">Renderer name.</param>
    /// <returns>True when supported.</returns>
    public static bool IsSupportedReadmeRenderer(string? renderer)
        => string.Equals(renderer, "SummaryTable", StringComparison.OrdinalIgnoreCase)
           || string.Equals(renderer, "ComparisonTable", StringComparison.OrdinalIgnoreCase);

    private static string WriteSamplesCsv(IEnumerable<BenchmarkSample> samples)
    {
        var rows = samples.ToArray();
        var variableHeaders = GetVariableHeaders(rows.Select(row => row.Variables));
        var metricHeaders = GetMetricHeaders(rows.Select(row => row.Metrics));
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", new[] { "Suite", "Scenario", "Operation", "Engine", "Host", "OS", "RunMode" }.Concat(variableHeaders).Concat(new[] { "Iteration", "Status", "DurationMs", "Reason" }).Concat(metricHeaders).Select(Cell)));
        foreach (var sample in rows)
        {
            var cells = new List<string>
            {
                Cell(sample.Suite),
                Cell(sample.Scenario),
                Cell(sample.Operation),
                Cell(sample.Engine),
                Cell(sample.Host),
                Cell(sample.Os),
                Cell(sample.RunMode)
            };
            cells.AddRange(variableHeaders.Select(header => Cell(sample.Variables.TryGetValue(header, out var value) ? value : null)));
            cells.Add(sample.Iteration.ToString(CultureInfo.InvariantCulture));
            cells.Add(sample.Status.ToString());
            cells.Add(Number(sample.DurationMs));
            cells.Add(Cell(sample.Reason));
            cells.AddRange(metricHeaders.Select(header => sample.Metrics.TryGetValue(header, out var value) ? Number(value) : string.Empty));
            builder.AppendLine(string.Join(",", cells));
        }

        return builder.ToString();
    }

    private static string WriteSummaryCsv(IEnumerable<BenchmarkSummaryRow> rows)
    {
        var summaryRows = rows.ToArray();
        var variableHeaders = GetVariableHeaders(summaryRows.Select(row => row.Variables));
        var metricHeaders = GetMetricHeaders(summaryRows.Select(row => row.Metrics));
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", new[] { "Suite", "Scenario", "Operation", "Engine", "Host", "OS", "RunMode" }.Concat(variableHeaders).Concat(new[] { "SampleCount", "FailureCount", "OutlierCount", "Status", "MedianMs", "MeanMs", "MinMs", "MaxMs", "P95Ms", "P99Ms", "StdDevMs", "StdErrMs", "FailureReasons" }).Concat(metricHeaders).Select(Cell)));
        foreach (var row in summaryRows)
        {
            var cells = new List<string>
            {
                Cell(row.Suite),
                Cell(row.Scenario),
                Cell(row.Operation),
                Cell(row.Engine),
                Cell(row.Host),
                Cell(row.Os),
                Cell(row.RunMode)
            };
            cells.AddRange(variableHeaders.Select(header => Cell(row.Variables.TryGetValue(header, out var value) ? value : null)));
            cells.Add(row.SampleCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.FailureCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.OutlierCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(Cell(row.Status));
            cells.Add(Number(row.MedianMs));
            cells.Add(Number(row.MeanMs));
            cells.Add(Number(row.MinMs));
            cells.Add(Number(row.MaxMs));
            cells.Add(Number(row.P95Ms));
            cells.Add(Number(row.P99Ms));
            cells.Add(Number(row.StdDevMs));
            cells.Add(Number(row.StdErrMs));
            cells.Add(Cell(FormatFailureReasons(row.FailureReasons)));
            cells.AddRange(metricHeaders.Select(header => row.Metrics.TryGetValue(header, out var value) ? Number(value) : string.Empty));
            builder.AppendLine(string.Join(",", cells));
        }

        return builder.ToString();
    }

    private static string[] GetVariableHeaders(IEnumerable<Dictionary<string, string?>> variables)
        => variables
            .SelectMany(row => row.Keys)
            .Where(key => !PowerShellBenchmarkRunner.IsBenchmarkColumnName(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] GetMetricHeaders(IEnumerable<Dictionary<string, double>> metrics)
        => metrics
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FormatFailureReasons(IReadOnlyDictionary<string, int> reasons)
        => string.Join(
            "; ",
            (reasons ?? new Dictionary<string, int>())
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Value.ToString(CultureInfo.InvariantCulture), "x ", k.Key)));

    private static string Number(double? value)
        => value.HasValue ? value.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(",", StringComparison.Ordinal) || text.Contains("\"", StringComparison.Ordinal) || text.Contains("\n", StringComparison.Ordinal)
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
