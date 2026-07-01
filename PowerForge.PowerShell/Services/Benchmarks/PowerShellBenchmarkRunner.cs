using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
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
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = previousRunspace is null ? RunspaceFactory.CreateRunspace() : null;
        if (runspace is not null)
        {
            runspace.Open();
            Runspace.DefaultRunspace = runspace;
        }

        try
        {
            return PlanInCurrentRunspace(suite);
        }
        finally
        {
            if (runspace is not null)
                Runspace.DefaultRunspace = previousRunspace;
        }
    }

    private PowerShellBenchmarkWorkItem[] PlanInCurrentRunspace(PowerShellBenchmarkSuite suite)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        ValidateSupportedAxes(suite);
        var cases = suite.Cases.Count == 0
            ? new[] { new PowerShellBenchmarkCase { Name = "Default" } }
            : suite.Cases.ToArray();
        var expanded = ExpandCases(cases, suite.Axes);
        var engineAxis = GetAxisValues(suite.Axes, "Engine") ?? suite.Engines.Select(e => (object?)e.Name).ToArray();
        var explicitOperationAxis = GetAxisValues(suite.Axes, "Operation");
        var currentHostLabel = GetCurrentHostLabel();
        var hostAxis = GetAxisValues(suite.Axes, "Host") ?? new object?[] { currentHostLabel };
        if (expanded.Count == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any runnable cases.");
        if (engineAxis.Length == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any engine values.");
        if (hostAxis.Length == 0) throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any host values.");
        var items = new List<PowerShellBenchmarkWorkItem>();

        foreach (var values in expanded)
        foreach (var engineValue in engineAxis)
        {
            var engineName = Convert.ToString(engineValue, CultureInfo.InvariantCulture) ?? string.Empty;
            var engine = suite.Engines.FirstOrDefault(e => string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase));
            if (engine is null)
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define engine '{engineName}'.");
            var operationAxis = explicitOperationAxis ?? engine.Operations.Keys.Cast<object?>().ToArray();
            if (operationAxis.Length == 0)
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define any operation values for engine '{engineName}'.");

            foreach (var operationValue in operationAxis)
            foreach (var hostValue in hostAxis)
            {
                var operationName = Convert.ToString(operationValue, CultureInfo.InvariantCulture) ?? string.Empty;
                var requestedHostName = Convert.ToString(hostValue, CultureInfo.InvariantCulture) ?? "Current";
                if (!IsCurrentHost(requestedHostName, currentHostLabel))
                    throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested host '{requestedHostName}', but this runner only supports the current PowerShell host. Use 'Current' or run the suite from the target host.");
                var hostName = NormalizeCurrentHost(requestedHostName, currentHostLabel);
                values["Engine"] = engineName;
                values["Operation"] = operationName;
                values["Host"] = hostName;
                var itemValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
                var isSkipped = ShouldSkip(suite.Skip, ToPsObject(itemValues));
                ScriptBlock? handler = null;
                if (!isSkipped && !engine.Operations.TryGetValue(operationName, out handler))
                    throw new InvalidOperationException($"Benchmark suite '{suite.Name}' does not define handler for engine '{engineName}' operation '{operationName}'.");

                items.Add(new PowerShellBenchmarkWorkItem
                {
                    Values = itemValues,
                    Scenario = GetScenarioName(values),
                    Engine = engineName,
                    Operation = operationName,
                    Host = hostName,
                    Handler = isSkipped ? ScriptBlock.Create(string.Empty) : handler!,
                    IsSkipped = isSkipped
                });
            }
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
        ValidateCurrentRunspaceProfile(suite);
        ValidateComparisons(suite);
        ValidateReadmeBlocks(suite);
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var started = DateTimeOffset.UtcNow;
        var samples = new List<BenchmarkSample>();
        var workItems = Plan(suite);
        ValidateComparisonWorkItems(suite, workItems);
        var runnable = new List<PowerShellBenchmarkWorkItem>();
        foreach (var item in workItems)
        {
            if (item.IsSkipped || ShouldSkip(suite.Skip, ToPsObject(item.Values)))
            {
                samples.Add(CreateSample(runId, suite, item, 0, BenchmarkSampleStatus.Skipped, 0, "Skipped by benchmark rule.", null));
                continue;
            }

            var warmupFailed = false;
            for (var warmup = 0; warmup < Math.Max(0, suite.WarmupCount); warmup++)
            {
                var warmupSample = InvokeMeasuredIteration(suite, item, ToPsObject(item.Values), -warmup - 1, runId, recordSample: false);
                if (warmupSample.Status == BenchmarkSampleStatus.Failed)
                {
                    samples.Add(warmupSample);
                    warmupFailed = true;
                    break;
                }
            }

            if (warmupFailed)
                continue;

            runnable.Add(item);
        }

        for (var iteration = 0; iteration < Math.Max(1, suite.IterationCount); iteration++)
        {
            foreach (var item in Rotate(runnable, iteration))
                samples.Add(InvokeMeasuredIteration(suite, item, ToPsObject(item.Values), iteration, runId, recordSample: true));
        }

        var summarizer = new BenchmarkSummaryService();
        var summary = summarizer.Summarize(samples);
        var result = new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples.ToArray(),
            Summary = summary,
            Comparison = Array.Empty<BenchmarkComparisonRow>(),
            Metadata = BuildMetadata(suite)
        };

        try
        {
            result.Comparison = suite.Comparisons
                .Where(c => !string.IsNullOrWhiteSpace(c.Baseline))
                .SelectMany(c => c.Metrics.SelectMany(m => summarizer.Compare(summary, c.Baseline, m)))
                .ToArray();
        }
        catch
        {
            WriteArtifacts(suite, result);
            throw;
        }

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
            MatrixPathSegment(item.Values),
            iteration.ToString(CultureInfo.InvariantCulture));
        var runObject = ToPsObject(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RunId"] = runId,
            ["Iteration"] = iteration,
            ["OutputRoot"] = suite.OutputRoot,
            ["OutputDirectory"] = outputDirectory,
            ["OutputPath"] = Path.Combine(outputDirectory, "output")
        });
        var durationMs = 0d;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            InvokeOptional(suite.Setup, caseObject, runObject);
            var data = CaptureData(InvokeOptional(suite.Data, caseObject, runObject));
            SetProperty(runObject, "Data", data);

            var stopwatch = Stopwatch.StartNew();
            InvokeStrict(item.Handler, caseObject, runObject);
            stopwatch.Stop();
            durationMs = stopwatch.Elapsed.TotalMilliseconds;
            SetProperty(runObject, "DurationMs", durationMs);

            if (!recordSample)
                return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Succeeded, durationMs, string.Empty, null);

            InvokeOptional(suite.Validate, caseObject, runObject);
            var metrics = CaptureMetrics(suite, caseObject, runObject);
            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Succeeded, durationMs, string.Empty, metrics);
        }
        catch (Exception ex) when (!IsPowerShellStopRequest(ex))
        {
            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Failed, durationMs, ex.InnerException?.Message ?? ex.Message, null);
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
            Os = GetOperatingSystemLabel(),
            RunMode = suite.RunMode,
            Iteration = iteration,
            Status = status,
            DurationMs = durationMs,
            Reason = reason,
            Variables = ToVariables(item.Values),
            Metrics = metrics ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        };

    private static bool ShouldSkip(ScriptBlock? skip, PSObject caseObject)
    {
        if (skip is null) return false;
        return InvokeStrict(skip, caseObject).Any(value => LanguagePrimitives.IsTrue(value));
    }

    private static bool IsPowerShellStopRequest(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PipelineStoppedException or OperationCanceledException)
                return true;
        }

        return false;
    }

    private static void ValidateSupportedAxes(PowerShellBenchmarkSuite suite)
    {
        if (GetAxisValues(suite.Axes, "OS") is not null)
            throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested an OS axis, but this runner only supports the current operating system. Run the suite on each target OS and compare imported results instead.");
    }

    private static void ValidateComparisons(PowerShellBenchmarkSuite suite)
    {
        var engineValues = GetAxisValues(suite.Axes, "Engine") ?? suite.Engines.Select(e => (object?)e.Name).ToArray();
        foreach (var comparison in suite.Comparisons)
        {
            if (string.IsNullOrWhiteSpace(comparison.Baseline))
                throw new InvalidOperationException("Benchmark comparison baseline is required.");
            if (!string.Equals(comparison.Dimension, "Engine", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Benchmark comparison dimension '{comparison.Dimension}' is not supported. Only Engine comparisons are supported.");
            if (!engineValues.Any(value => string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), comparison.Baseline, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Benchmark comparison baseline engine '{comparison.Baseline}' is not declared by suite '{suite.Name}'.");
        }
    }

    private static void ValidateReadmeBlocks(PowerShellBenchmarkSuite suite)
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

    private static void ValidateCurrentRunspaceProfile(PowerShellBenchmarkSuite suite)
    {
        if (suite.Profile == PowerShellBenchmarkProfileKind.TemporaryLocalUser)
            throw new InvalidOperationException("Benchmark profile 'TemporaryLocalUser' requires a file-backed Invoke-BenchmarkSuite run so the benchmark spec can be re-evaluated inside the temporary Windows user profile.");
    }

    private static void ValidateComparisonWorkItems(PowerShellBenchmarkSuite suite, IReadOnlyList<PowerShellBenchmarkWorkItem> workItems)
    {
        foreach (var comparison in suite.Comparisons)
        {
            if (string.IsNullOrWhiteSpace(comparison.Baseline))
                continue;
            if (!string.Equals(comparison.Dimension, "Engine", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var group in workItems.GroupBy(item => ComparisonGroupKey(suite.Name, item), StringComparer.OrdinalIgnoreCase))
            {
                if (group.All(item => item.IsSkipped))
                    continue;
                if (group.Any(item => !item.IsSkipped && string.Equals(item.Engine, comparison.Baseline, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var first = group.First();
                throw new InvalidOperationException($"Benchmark comparison baseline engine '{comparison.Baseline}' has no runnable work items for suite '{suite.Name}', scenario '{first.Scenario}', operation '{first.Operation}', host '{first.Host}', variables '{FormatVariables(ToVariables(first.Values))}'. Check skip rules or choose a runnable baseline.");
            }
        }
    }

    private static string ComparisonGroupKey(string suite, PowerShellBenchmarkWorkItem item)
        => string.Join("\u001f", suite, item.Scenario, item.Operation, item.Host, GetOperatingSystemLabel(), FormatVariables(ToVariables(item.Values)));

    private static IEnumerable<PowerShellBenchmarkWorkItem> Rotate(IReadOnlyList<PowerShellBenchmarkWorkItem> items, int iteration)
    {
        if (items.Count == 0)
            yield break;
        var offset = iteration % items.Count;
        for (var i = 0; i < items.Count; i++)
            yield return items[(offset + i) % items.Count];
    }

    private static Collection<PSObject> InvokeOptional(ScriptBlock? block, PSObject caseObject, PSObject runObject)
        => block is null ? new Collection<PSObject>() : InvokeStrict(block, caseObject, runObject);

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
            var value = InvokeStrict(metric.ScriptBlock, caseObject, runObject).FirstOrDefault()?.BaseObject;
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
            ["os"] = Environment.OSVersion.ToString(),
            ["profile"] = suite.Profile.ToString(),
            ["cleanup"] = suite.Cleanup.ToString()
        };

    private static string PSVersionInfo()
        => Convert.ToString(PSObject.AsPSObject(typeof(PSObject).Assembly.GetName().Version).BaseObject, CultureInfo.InvariantCulture) ?? string.Empty;

    private static object? PSVersionInfoValue(string name)
    {
        using var ps = PowerShell.Create();
        return ps.AddScript($"$PSVersionTable.{name}").Invoke().FirstOrDefault()?.BaseObject;
    }

    private static void WriteArtifacts(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
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

    private static void UpdateReadmeBlocks(PowerShellBenchmarkSuite suite, BenchmarkRunResult result)
    {
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

    private static bool IsSupportedReadmeRenderer(string? renderer)
        => string.Equals(renderer, "SummaryTable", StringComparison.OrdinalIgnoreCase)
           || string.Equals(renderer, "ComparisonTable", StringComparison.OrdinalIgnoreCase);

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

    private static bool IsCurrentHost(string host, string currentHostLabel)
        => string.IsNullOrWhiteSpace(host)
           || string.Equals(host, "Current", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "CurrentHost", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, currentHostLabel, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCurrentHost(string host, string currentHostLabel)
        => string.IsNullOrWhiteSpace(host)
           || string.Equals(host, "Current", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "CurrentHost", StringComparison.OrdinalIgnoreCase)
            ? currentHostLabel
            : host;

    private static string GetCurrentHostLabel()
    {
        var edition = Convert.ToString(PSVersionInfoValue("PSEdition"), CultureInfo.InvariantCulture);
        var version = Convert.ToString(PSVersionInfoValue("PSVersion"), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(edition))
            edition = "PowerShell";
        return string.IsNullOrWhiteSpace(version) ? edition! : string.Concat(edition, "-", version);
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var text = string.IsNullOrWhiteSpace(value) ? "_" : value;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        return builder.ToString();
    }

    private static string MatrixPathSegment(IReadOnlyDictionary<string, object?> values)
    {
        var text = string.Join(
            "_",
            values
                .Where(k => !IsBuiltInPathValue(k.Key))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(EscapeMatrixPathPart(k.Key), "=", EscapeMatrixPathPart(Convert.ToString(k.Value, CultureInfo.InvariantCulture) ?? string.Empty))));
        return SafePathSegment(string.IsNullOrWhiteSpace(text) ? "matrix" : text);
    }

    private static string EscapeMatrixPathPart(string value)
    {
        var escaped = Uri.EscapeDataString(value ?? string.Empty);
        return escaped.Replace("_", "%5F");
    }

    private static string GetOperatingSystemLabel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return Environment.OSVersion.Platform.ToString();
    }

    private static bool IsBuiltInPathValue(string key)
        => string.Equals(key, "Scenario", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Engine", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Operation", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase);

    private static PSObject ToPsObject(IReadOnlyDictionary<string, object?> values)
    {
        var expando = new System.Dynamic.ExpandoObject();
        var psObject = new PSObject(expando);
        var members = (IDictionary<string, object?>)expando;
        foreach (var entry in values)
            members[entry.Key] = entry.Value;
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
        var rows = samples.ToArray();
        var variableHeaders = GetVariableHeaders(rows.Select(row => row.Variables));
        var metricHeaders = GetMetricHeaders(rows.Select(row => row.Metrics));
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", new[] { "Suite", "Scenario", "Operation", "Engine", "Host", "OS" }.Concat(variableHeaders).Concat(new[] { "Iteration", "Status", "DurationMs", "Reason" }).Concat(metricHeaders)));
        foreach (var sample in rows)
        {
            var cells = new List<string>
            {
                Cell(sample.Suite),
                Cell(sample.Scenario),
                Cell(sample.Operation),
                Cell(sample.Engine),
                Cell(sample.Host),
                Cell(sample.Os)
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
        builder.AppendLine(string.Join(",", new[] { "Suite", "Scenario", "Operation", "Engine", "Host", "OS" }.Concat(variableHeaders).Concat(new[] { "SampleCount", "FailureCount", "Status", "MedianMs", "MeanMs", "MinMs", "MaxMs" }).Concat(metricHeaders)));
        foreach (var row in summaryRows)
        {
            var cells = new List<string>
            {
                Cell(row.Suite),
                Cell(row.Scenario),
                Cell(row.Operation),
                Cell(row.Engine),
                Cell(row.Host),
                Cell(row.Os)
            };
            cells.AddRange(variableHeaders.Select(header => Cell(row.Variables.TryGetValue(header, out var value) ? value : null)));
            cells.Add(row.SampleCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.FailureCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(Cell(row.Status));
            cells.Add(Number(row.MedianMs));
            cells.Add(Number(row.MeanMs));
            cells.Add(Number(row.MinMs));
            cells.Add(Number(row.MaxMs));
            cells.AddRange(metricHeaders.Select(header => row.Metrics.TryGetValue(header, out var value) ? Number(value) : string.Empty));
            builder.AppendLine(string.Join(",", cells));
        }

        return builder.ToString();
    }

    private static Collection<PSObject> InvokeStrict(ScriptBlock block, params object[] args)
    {
        var variables = new List<PSVariable>
        {
            new("ErrorActionPreference", ActionPreference.Stop),
            new("PSNativeCommandUseErrorActionPreference", true)
        };
        return NativeExitAwareInvokeWrapper.InvokeWithContext(functionsToDefine: null, variablesToDefine: variables, new object[] { block, args });
    }

    private static readonly ScriptBlock NativeExitAwareInvokeWrapper = ScriptBlock.Create("""
param([scriptblock] $Block, [object[]] $Arguments)
$previousLastExitCode = $global:LASTEXITCODE
$global:LASTEXITCODE = 0
try {
    & $Block @Arguments
    $nativeExitCode = $global:LASTEXITCODE
    if ($null -ne $nativeExitCode -and $nativeExitCode -ne 0) {
        throw "Native command exited with code $nativeExitCode."
    }
}
finally {
    if ($null -eq $previousLastExitCode) {
        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    } else {
        $global:LASTEXITCODE = $previousLastExitCode
    }
}
""");

    private static string[] GetVariableHeaders(IEnumerable<Dictionary<string, string?>> variables)
        => variables
            .SelectMany(row => row.Keys)
            .Where(key => !IsBenchmarkColumn(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] GetMetricHeaders(IEnumerable<Dictionary<string, double>> metrics)
        => metrics
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            "\u001e",
            variables
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));

    private static Dictionary<string, string?> ToVariables(IReadOnlyDictionary<string, object?> values)
        => values
            .Where(k => !IsBenchmarkColumn(k.Key))
            .ToDictionary(k => k.Key, k => (string?)Convert.ToString(k.Value, CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);

    private static bool IsBenchmarkColumn(string key)
        => IsBuiltInPathValue(key)
           || string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Suite", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "RunId", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "OS", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "RunMode", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Iteration", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Status", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "DurationMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Reason", StringComparison.OrdinalIgnoreCase);

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
