using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PowerForge;

/// <summary>
/// Executes PowerShell-authored benchmark suites in the current host process.
/// </summary>
public sealed class PowerShellBenchmarkRunner
{
    /// <summary>
    /// Expands a suite into concrete work items without executing measurements.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <returns>Resolved work items.</returns>
    public PowerShellBenchmarkWorkItem[] Plan(PowerShellBenchmarkSuite suite)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        var cases = suite.Cases.Count == 0
            ? new[] { new PowerShellBenchmarkCase { Name = "Default" } }
            : suite.Cases.ToArray();
        var expanded = ExpandCases(cases, suite.Axes);
        var engineAxis = GetAxisValues(suite.Axes, "Engine") ?? suite.Engines.Select(e => (object?)e.Name).ToArray();
        var operationAxis = GetAxisValues(suite.Axes, "Operation") ?? suite.Engines.SelectMany(e => e.Operations.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object?>().ToArray();
        var hostAxis = GetAxisValues(suite.Axes, "Host") ?? new object?[] { "Current" };
        if (expanded.Count == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any runnable cases.");
        if (engineAxis.Length == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any engine values.");
        if (operationAxis.Length == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any operation values.");
        if (hostAxis.Length == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any host values.");
        var items = new List<PowerShellBenchmarkWorkItem>();

        foreach (var values in expanded)
        foreach (var engineValue in engineAxis)
        foreach (var operationValue in operationAxis)
        foreach (var hostValue in hostAxis)
        {
            var engineName = Convert.ToString(engineValue, CultureInfo.InvariantCulture) ?? string.Empty;
            var operationName = Convert.ToString(operationValue, CultureInfo.InvariantCulture) ?? string.Empty;
            var hostName = Convert.ToString(hostValue, CultureInfo.InvariantCulture) ?? "Current";
            if (!IsCurrentHost(hostName))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested host '{hostName}', but this runner only supports the current PowerShell host. Use 'Current' or run the suite from the target host.");
            var engine = suite.Engines.FirstOrDefault(e => string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase));
            if (engine is null || !engine.Operations.TryGetValue(operationName, out var handler))
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define handler for engine '{engineName}' operation '{operationName}'.");

            values["Engine"] = engineName;
            values["Operation"] = operationName;
            values["Host"] = hostName;
            items.Add(new PowerShellBenchmarkWorkItem
            {
                Values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase),
                Scenario = GetScenarioName(values),
                Engine = engineName,
                Operation = operationName,
                Host = hostName,
                Handler = handler
            });
        }

        return items.Count == 0
            ? throw new InvalidOperationException($"Benchmark suite '{suite.Name}' did not produce any work items.")
            : items.ToArray();
    }

    /// <summary>
    /// Executes a benchmark suite and writes requested artifacts.
    /// </summary>
    /// <param name="suite">Benchmark suite.</param>
    /// <returns>Run result.</returns>
    public BenchmarkRunResult Run(PowerShellBenchmarkSuite suite)
    {
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = previousRunspace is null ? RunspaceFactory.CreateRunspace() : null;
        if (runspace is not null)
        {
            runspace.Open();
            Runspace.DefaultRunspace = runspace;
        }

        try
        {
            return RunInCurrentRunspace(suite);
        }
        finally
        {
            if (runspace is not null)
                Runspace.DefaultRunspace = previousRunspace;
        }
    }

    private BenchmarkRunResult RunInCurrentRunspace(PowerShellBenchmarkSuite suite)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var started = DateTimeOffset.UtcNow;
        var samples = new List<BenchmarkSample>();
        var workItems = Plan(suite);
        foreach (var item in workItems)
        {
            var caseObject = ToPsObject(item.Values);
            if (ShouldSkip(suite.Skip, caseObject))
            {
                samples.Add(CreateSample(runId, suite, item, 0, BenchmarkSampleStatus.Skipped, 0, "Skipped by benchmark rule.", null));
                continue;
            }

            var warmupFailed = false;
            for (var warmup = 0; warmup < Math.Max(0, suite.WarmupCount); warmup++)
            {
                var warmupSample = InvokeMeasuredIteration(suite, item, caseObject, -warmup - 1, runId, recordSample: false);
                if (warmupSample.Status == BenchmarkSampleStatus.Failed)
                {
                    samples.Add(warmupSample);
                    warmupFailed = true;
                    break;
                }
            }

            if (warmupFailed)
                continue;

            for (var iteration = 0; iteration < Math.Max(1, suite.IterationCount); iteration++)
                samples.Add(InvokeMeasuredIteration(suite, item, caseObject, iteration, runId, recordSample: true));
        }

        var summarizer = new BenchmarkSummaryService();
        var summary = summarizer.Summarize(samples);
        var comparisons = suite.Comparisons
            .Where(c => string.Equals(c.Dimension, "Engine", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Baseline))
            .SelectMany(c => c.Metrics.SelectMany(m => summarizer.Compare(summary, c.Baseline, m)))
            .ToArray();

        var result = new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples.ToArray(),
            Summary = summary,
            Comparison = comparisons,
            Metadata = BuildMetadata(suite)
        };

        WriteArtifacts(suite, result);
        UpdateReadmeBlocks(suite, result);
        return result;
    }

    private BenchmarkSample InvokeMeasuredIteration(
        PowerShellBenchmarkSuite suite,
        PowerShellBenchmarkWorkItem item,
        PSObject caseObject,
        int iteration,
        string runId,
        bool recordSample)
    {
        var outputDirectory = Path.Combine(
            suite.OutputRoot,
            runId,
            SafePathSegment(item.Scenario),
            SafePathSegment(item.Engine),
            SafePathSegment(item.Operation),
            iteration.ToString(CultureInfo.InvariantCulture));
        var runObject = ToPsObject(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RunId"] = runId,
            ["Iteration"] = iteration,
            ["OutputRoot"] = suite.OutputRoot,
            ["OutputDirectory"] = outputDirectory,
            ["OutputPath"] = Path.Combine(outputDirectory, "output")
        });

        try
        {
            Directory.CreateDirectory(outputDirectory);
            InvokeOptional(suite.Setup, caseObject, runObject);
            var data = CaptureData(InvokeOptional(suite.Data, caseObject, runObject));
            SetProperty(runObject, "Data", data);

            var stopwatch = Stopwatch.StartNew();
            item.Handler.Invoke(caseObject, runObject);
            stopwatch.Stop();

            InvokeOptional(suite.Validate, caseObject, runObject);
            var metrics = CaptureMetrics(suite, caseObject, runObject);
            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Succeeded, stopwatch.Elapsed.TotalMilliseconds, string.Empty, metrics);
        }
        catch (Exception ex)
        {
            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Failed, 0, ex.InnerException?.Message ?? ex.Message, null);
        }
    }

    private static BenchmarkSample CreateSample(
        string runId,
        PowerShellBenchmarkSuite suite,
        PowerShellBenchmarkWorkItem item,
        int iteration,
        BenchmarkSampleStatus status,
        double durationMs,
        string reason,
        Dictionary<string, double>? metrics)
        => new()
        {
            RunId = runId,
            Suite = suite.Name,
            Scenario = item.Scenario,
            Operation = item.Operation,
            Engine = item.Engine,
            Host = item.Host,
            Os = Environment.OSVersion.Platform.ToString(),
            RunMode = suite.RunMode,
            Iteration = iteration,
            Status = status,
            DurationMs = durationMs,
            Reason = reason,
            Variables = item.Values.ToDictionary(k => k.Key, k => (string?)Convert.ToString(k.Value, CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase),
            Metrics = metrics ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        };

    private static bool ShouldSkip(ScriptBlock? skip, PSObject caseObject)
    {
        if (skip is null) return false;
        return skip.Invoke(caseObject).Any(value => LanguagePrimitives.IsTrue(value));
    }

    private static Collection<PSObject> InvokeOptional(ScriptBlock? block, PSObject caseObject, PSObject runObject)
        => block is null ? new Collection<PSObject>() : block.Invoke(caseObject, runObject);

    private static object? CaptureData(IReadOnlyList<PSObject> values)
    {
        if (values.Count == 0) return null;
        if (values.Count == 1) return values[0].BaseObject;
        return values.Select(v => v.BaseObject).ToArray();
    }

    private static Dictionary<string, double> CaptureMetrics(PowerShellBenchmarkSuite suite, PSObject caseObject, PSObject runObject)
    {
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var metric in suite.Metrics)
        {
            var value = metric.ScriptBlock.Invoke(caseObject, runObject).FirstOrDefault()?.BaseObject;
            if (TryConvertToDouble(value, out var number))
                metrics[metric.Name] = number;
        }

        return metrics;
    }

    private static bool TryConvertToDouble(object? value, out double number)
    {
        number = 0;
        if (value is null) return false;
        if (value is double d) { number = d; return true; }
        if (value is float f) { number = f; return true; }
        if (value is decimal m) { number = (double)m; return true; }
        if (value is int i) { number = i; return true; }
        if (value is long l) { number = l; return true; }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static Dictionary<string, string> BuildMetadata(PowerShellBenchmarkSuite suite)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["suite"] = suite.Name,
            ["pwsh"] = PSVersionInfo(),
            ["machine"] = Environment.MachineName,
            ["os"] = Environment.OSVersion.ToString()
        };

    private static string PSVersionInfo()
        => Convert.ToString(PSObject.AsPSObject(typeof(PSObject).Assembly.GetName().Version).BaseObject, CultureInfo.InvariantCulture) ?? string.Empty;

    private static void WriteArtifacts(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
    {
        var outputRoot = Path.Combine(suite.OutputRoot, result.RunId);
        Directory.CreateDirectory(outputRoot);
        if (suite.Artifacts.HasFlag(BenchmarkArtifactKind.Json))
        {
            var samplesPath = Path.Combine(outputRoot, "samples.json");
            var summaryPath = Path.Combine(outputRoot, "summary.json");
            var comparisonPath = Path.Combine(outputRoot, "comparison.json");
            var runReportPath = Path.Combine(outputRoot, "run-report.json");
            result.Artifacts["samples.json"] = samplesPath;
            result.Artifacts["summary.json"] = summaryPath;
            result.Artifacts["comparison.json"] = comparisonPath;
            result.Artifacts["run-report.json"] = runReportPath;
            BenchmarkJson.Write(samplesPath, result.Samples);
            BenchmarkJson.Write(summaryPath, result.Summary);
            BenchmarkJson.Write(comparisonPath, result.Comparison);
            BenchmarkJson.Write(runReportPath, result);
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
    }

    private static void UpdateReadmeBlocks(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
    {
        var updater = new BenchmarkDocumentUpdater();
        var renderer = new BenchmarkMarkdownRenderer();
        foreach (var block in suite.ReadmeBlocks)
        {
            var markdown = string.Equals(block.Renderer, "ComparisonTable", StringComparison.OrdinalIgnoreCase)
                ? renderer.RenderComparisonTable(result.Comparison)
                : renderer.RenderSummaryTable(result.Summary);
            updater.UpdateBlock(block.Path, block.BlockId, markdown);
        }
    }

    private static List<Dictionary<string, object?>> ExpandCases(IEnumerable<PowerShellBenchmarkCase> cases, IEnumerable<PowerShellBenchmarkAxis> axes)
    {
        var matrixAxes = axes
            .Where(a => !string.Equals(a.Name, "Engine", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.Name, "Operation", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.Name, "Host", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var expanded = new List<Dictionary<string, object?>>();
        foreach (var benchmarkCase in cases)
        {
            var seed = new Dictionary<string, object?>(benchmarkCase.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["Scenario"] = benchmarkCase.Name
            };
            ExpandAxis(seed, matrixAxes, 0, expanded);
        }

        return expanded;
    }

    private static void ExpandAxis(Dictionary<string, object?> current, PowerShellBenchmarkAxis[] axes, int index, List<Dictionary<string, object?>> result)
    {
        if (index >= axes.Length)
        {
            result.Add(new Dictionary<string, object?>(current, StringComparer.OrdinalIgnoreCase));
            return;
        }

        foreach (var value in axes[index].Values)
        {
            current[axes[index].Name] = value;
            ExpandAxis(current, axes, index + 1, result);
        }
    }

    private static object?[]? GetAxisValues(IEnumerable<PowerShellBenchmarkAxis> axes, string name)
        => axes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))?.Values.ToArray();

    private static string GetScenarioName(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue("Scenario", out var scenario) && !string.IsNullOrWhiteSpace(Convert.ToString(scenario, CultureInfo.InvariantCulture)))
            return Convert.ToString(scenario, CultureInfo.InvariantCulture)!;
        if (values.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(Convert.ToString(name, CultureInfo.InvariantCulture)))
            return Convert.ToString(name, CultureInfo.InvariantCulture)!;
        return "Scenario";
    }

    private static bool IsCurrentHost(string host)
        => string.IsNullOrWhiteSpace(host)
           || string.Equals(host, "Current", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "CurrentHost", StringComparison.OrdinalIgnoreCase);

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var text = string.IsNullOrWhiteSpace(value) ? "_" : value;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        return builder.ToString();
    }

    private static PSObject ToPsObject(IReadOnlyDictionary<string, object?> values)
    {
        var psObject = new PSObject();
        foreach (var entry in values)
            psObject.Properties.Add(new PSNoteProperty(entry.Key, entry.Value));
        return psObject;
    }

    private static object? GetProperty(PSObject value, string name)
        => value.Properties[name]?.Value;

    private static void SetProperty(PSObject value, string name, object? propertyValue)
    {
        var existing = value.Properties[name];
        if (existing is null)
            value.Properties.Add(new PSNoteProperty(name, propertyValue));
        else
            existing.Value = propertyValue;
    }

    private static string WriteSamplesCsv(IEnumerable<BenchmarkSample> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Suite,Scenario,Operation,Engine,Host,Iteration,Status,DurationMs,Reason");
        foreach (var sample in samples)
            builder.AppendLine(string.Join(",", Cell(sample.Suite), Cell(sample.Scenario), Cell(sample.Operation), Cell(sample.Engine), Cell(sample.Host), sample.Iteration.ToString(CultureInfo.InvariantCulture), sample.Status, sample.DurationMs.ToString("0.###", CultureInfo.InvariantCulture), Cell(sample.Reason)));
        return builder.ToString();
    }

    private static string WriteSummaryCsv(IEnumerable<BenchmarkSummaryRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Suite,Scenario,Operation,Engine,Host,SampleCount,FailureCount,Status,MedianMs,MeanMs,MinMs,MaxMs");
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", Cell(row.Suite), Cell(row.Scenario), Cell(row.Operation), Cell(row.Engine), Cell(row.Host), row.SampleCount.ToString(CultureInfo.InvariantCulture), row.FailureCount.ToString(CultureInfo.InvariantCulture), Cell(row.Status), Number(row.MedianMs), Number(row.MeanMs), Number(row.MinMs), Number(row.MaxMs)));
        return builder.ToString();
    }

    private static string Number(double? value)
        => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(",", StringComparison.Ordinal) || text.Contains("\"", StringComparison.Ordinal) || text.Contains("\n", StringComparison.Ordinal)
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
