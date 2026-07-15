using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;

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
            return PlanInCurrentRunspace(suite, allowExternalHosts: true);
        }
        finally
        {
            if (runspace is not null)
                Runspace.DefaultRunspace = previousRunspace;
        }
    }

    private PowerShellBenchmarkWorkItem[] PlanInCurrentRunspace(PowerShellBenchmarkSuite suite, bool allowExternalHosts)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        ValidateUniqueEngineNames(suite);
        ValidateUniqueAxisNames(suite);
        ValidateSupportedAxes(suite);
        var cases = suite.Cases.Count == 0
            ? new[] { new PowerShellBenchmarkCase { Name = "Default" } }
            : suite.Cases.ToArray();
        ValidateMetricNames(suite);
        ValidateCaseVariables(suite, cases);
        ValidateAxisCaseValueCollisions(suite, cases);
        ValidateMetricVariableCollisions(suite, cases);
        var expanded = ExpandCases(cases, suite.Axes);
        ValidateUniqueExpandedCaseLanes(suite, expanded);
        var engineAxis = GetAxisValues(suite.Axes, "Engine") ?? suite.Engines.Select(e => (object?)e.Name).ToArray();
        var explicitOperationAxis = GetAxisValues(suite.Axes, "Operation");
        var currentHostLabel = PowerShellBenchmarkHostRuntime.GetCurrentHostLabel();
        var hostAxis = GetAxisValues(suite.Axes, "Host") ?? new object?[] { currentHostLabel };
        var planningProfile = (suite.PlanningProfile ?? suite.Profile).ToString();
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
                if (!PowerShellBenchmarkHostRuntime.IsCurrentHost(requestedHostName, currentHostLabel) && !allowExternalHosts)
                    throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested host '{requestedHostName}', but this runner only supports the current PowerShell host. Use 'Current' or run the suite from the target host.");
                var hostName = PowerShellBenchmarkHostRuntime.IsCurrentHost(requestedHostName, currentHostLabel)
                    ? PowerShellBenchmarkHostRuntime.NormalizeCurrentHost(requestedHostName, currentHostLabel)
                    : requestedHostName;
                values["Profile"] = planningProfile;
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
        PowerShellBenchmarkArtifactWriter.ValidateReadmeBlocks(suite);
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var started = DateTimeOffset.UtcNow;
        var samples = new List<BenchmarkSample>();
        var workItems = PlanInCurrentRunspace(suite, allowExternalHosts: false);
        ValidateComparisonWorkItems(suite, workItems);
        var runnable = new List<PowerShellBenchmarkWorkItem>();
        foreach (var item in workItems)
        {
            if (item.IsSkipped)
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
            foreach (var item in OrderWorkItems(runnable, iteration, suite.RunOrder))
            {
                samples.Add(InvokeMeasuredIteration(suite, item, ToPsObject(item.Values), iteration, runId, recordSample: true));
                ApplyCooldown(suite);
            }
        }

        var summarizer = new BenchmarkSummaryService();
        var summary = summarizer.Summarize(samples, suite.OutlierMode);
        var result = new BenchmarkRunResult
        {
            RunId = runId,
            Suite = suite.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Samples = samples.ToArray(),
            Summary = summary,
            Comparison = Array.Empty<BenchmarkComparisonRow>(),
            Metadata = PowerShellBenchmarkEnvironmentMetadata.Build(suite)
        };

        try
        {
            result.Comparison = PowerShellBenchmarkComparisonEvaluator.Build(suite, summary);
            PowerShellBenchmarkComparisonEvaluator.ValidateGates(suite, summary);
        }
        catch
        {
            PowerShellBenchmarkArtifactWriter.WriteArtifacts(suite, result);
            throw;
        }

        PowerShellBenchmarkArtifactWriter.WriteArtifacts(suite, result);
        PowerShellBenchmarkArtifactWriter.UpdateReadmeBlocks(suite, result);
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
            PowerShellBenchmarkPathSegments.Value(item.Scenario),
            PowerShellBenchmarkPathSegments.Value(item.Engine),
            PowerShellBenchmarkPathSegments.Value(item.Operation),
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
        var stage = "Setup";
        Stopwatch? operationStopwatch = null;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            InvokeOptional(suite.Setup, caseObject, runObject);
            stage = "Data";
            var data = CaptureData(InvokeOptional(suite.Data, caseObject, runObject));
            SetProperty(runObject, "Data", data);

            stage = iteration < 0 ? "Warmup operation" : "Operation";
            operationStopwatch = Stopwatch.StartNew();
            InvokeStrict(item.Handler, caseObject, runObject);
            operationStopwatch.Stop();
            durationMs = operationStopwatch.Elapsed.TotalMilliseconds;
            SetProperty(runObject, "DurationMs", durationMs);

            if (!recordSample)
                return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Succeeded, durationMs, string.Empty, null);

            stage = "Validation";
            InvokeOptional(suite.Validate, caseObject, runObject);
            stage = "Metrics";
            var metrics = CaptureMetrics(suite, caseObject, runObject);
            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Succeeded, durationMs, string.Empty, metrics);
        }
        catch (Exception ex) when (!IsPowerShellStopRequest(ex))
        {
            if (operationStopwatch is { IsRunning: true })
            {
                operationStopwatch.Stop();
                durationMs = operationStopwatch.Elapsed.TotalMilliseconds;
                SetProperty(runObject, "DurationMs", durationMs);
            }

            return CreateSample(runId, suite, item, iteration, BenchmarkSampleStatus.Failed, durationMs, FormatFailureReason(stage, ex), null);
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

    private static string FormatFailureReason(string stage, Exception exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        var detail = new List<string> { string.Concat(stage, " failed: ", message) };
        var errorRecord = GetErrorRecord(exception);
        var invocation = errorRecord?.InvocationInfo;
        if (invocation is not null)
        {
            if (!string.IsNullOrWhiteSpace(invocation.ScriptName))
                detail.Add($"Location: {invocation.ScriptName}:{invocation.ScriptLineNumber}:{invocation.OffsetInLine}");
            if (!string.IsNullOrWhiteSpace(invocation.Line))
                detail.Add($"Line: {invocation.Line.Trim()}");
            if (!string.IsNullOrWhiteSpace(invocation.PositionMessage))
                detail.Add(invocation.PositionMessage.Trim());
        }

        return string.Join(Environment.NewLine, detail.Distinct(StringComparer.Ordinal));
    }

    private static ErrorRecord? GetErrorRecord(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is RuntimeException runtimeException && runtimeException.ErrorRecord is not null)
                return runtimeException.ErrorRecord;
            if (current is ActionPreferenceStopException actionPreferenceStopException && actionPreferenceStopException.ErrorRecord is not null)
                return actionPreferenceStopException.ErrorRecord;
        }

        return null;
    }

    private static void ValidateSupportedAxes(PowerShellBenchmarkSuite suite)
    {
        if (GetAxisValues(suite.Axes, "OS") is not null)
            throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested an OS axis, but this runner only supports the current operating system. Run the suite on each target OS and compare imported results instead.");
        if (GetAxisValues(suite.Axes, "RunMode") is not null)
            throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested a RunMode axis, but RunMode is suite metadata. Set suite RunMode once, or use a different matrix variable name.");
        if (GetAxisValues(suite.Axes, "Profile") is not null)
            throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested a Profile axis, but Profile is suite metadata. Set suite Profile once, or use a different matrix variable name.");
        foreach (var axis in suite.Axes)
        {
            if (ReservedMatrixAxisNames.Contains(axis.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' requested reserved matrix axis '{axis.Name}'. Use a different matrix variable name.");
        }
    }

    private static void ValidateUniqueEngineNames(PowerShellBenchmarkSuite suite)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var engine in suite.Engines)
        {
            if (string.IsNullOrWhiteSpace(engine.Name))
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' defines an engine without a name.");
            if (!names.Add(engine.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' defines duplicate engine '{engine.Name}'. Engine names must be unique ignoring case.");
        }
    }

    private static void ValidateUniqueAxisNames(PowerShellBenchmarkSuite suite)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var axis in suite.Axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name))
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' defines a matrix axis without a name.");
            if (!names.Add(axis.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' defines duplicate matrix axis '{axis.Name}'. Axis names must be unique ignoring case.");
            ValidateAxisValues(suite, axis);
        }
    }

    private static void ValidateAxisValues(PowerShellBenchmarkSuite suite, PowerShellBenchmarkAxis axis)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in axis.Values)
        {
            var key = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!values.Add(key))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' matrix axis '{axis.Name}' contains duplicate value '{key}'. Axis values must be unique ignoring case so path-backed lanes cannot collide.");
        }
    }

    private static void ValidateCaseVariables(PowerShellBenchmarkSuite suite, IEnumerable<PowerShellBenchmarkCase> cases)
    {
        foreach (var benchmarkCase in cases)
        foreach (var key in benchmarkCase.Values.Keys)
        {
            if (ReservedCaseVariableNames.Contains(key))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' case '{benchmarkCase.Name}' uses reserved case variable '{key}'. Use a different case variable name.");
        }
    }

    private static void ValidateMetricNames(PowerShellBenchmarkSuite suite)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metric in suite.Metrics)
        {
            if (string.IsNullOrWhiteSpace(metric.Name))
                throw new InvalidOperationException($"Benchmark suite '{suite.Name}' defines a metric without a name.");
            if (!names.Add(metric.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' defines duplicate metric '{metric.Name}'. Metric names must be unique ignoring case.");
            if (IsBenchmarkColumn(metric.Name) || ReservedMatrixAxisNames.Contains(metric.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' metric '{metric.Name}' conflicts with a built-in benchmark artifact column. Use a different metric name.");
        }
    }

    private static void ValidateAxisCaseValueCollisions(PowerShellBenchmarkSuite suite, IEnumerable<PowerShellBenchmarkCase> cases)
    {
        var matrixAxes = suite.Axes
            .Where(axis => !IsBuiltInPathValue(axis.Name))
            .Select(axis => axis.Name)
            .ToArray();
        if (matrixAxes.Length == 0)
            return;

        var axisNames = new HashSet<string>(matrixAxes, StringComparer.OrdinalIgnoreCase);
        foreach (var benchmarkCase in cases)
        foreach (var key in benchmarkCase.Values.Keys)
        {
            if (axisNames.Contains(key))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' matrix axis '{key}' conflicts with case '{benchmarkCase.Name}' variable '{key}'. Use either a case variable or a matrix axis for that value, not both.");
        }
    }

    private static void ValidateMetricVariableCollisions(PowerShellBenchmarkSuite suite, IEnumerable<PowerShellBenchmarkCase> cases)
    {
        if (suite.Metrics.Count == 0)
            return;

        var variableNames = suite.Axes
            .Where(axis => !IsBuiltInPathValue(axis.Name))
            .Select(axis => axis.Name)
            .Concat(cases.SelectMany(benchmarkCase => benchmarkCase.Values.Keys))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var metric in suite.Metrics)
        {
            if (variableNames.Contains(metric.Name))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' metric '{metric.Name}' conflicts with a matrix or case variable of the same name. Use distinct names so CSV artifacts can round-trip.");
        }
    }

    private static void ValidateUniqueExpandedCaseLanes(PowerShellBenchmarkSuite suite, IEnumerable<Dictionary<string, object?>> expanded)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var values in expanded)
        {
            var key = ExpandedCaseLaneKey(values);
            var scenario = GetScenarioName(values);
            if (seen.TryGetValue(key, out var existing))
                throw new NotSupportedException($"Benchmark suite '{suite.Name}' expands duplicate case lane '{existing}'. Case lanes must be unique; use IterationCount to repeat the same lane.");
            seen[key] = scenario;
        }
    }

    private static string ExpandedCaseLaneKey(IReadOnlyDictionary<string, object?> values)
        => string.Join(
            "\u001f",
            values
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(
                    PowerShellBenchmarkPathSegments.Value(k.Key),
                    "\u001e",
                    PowerShellBenchmarkPathSegments.Value(Convert.ToString(k.Value, CultureInfo.InvariantCulture)))));

    private static void ValidateComparisons(PowerShellBenchmarkSuite suite)
    {
        var engineValues = GetAxisValues(suite.Axes, "Engine") ?? suite.Engines.Select(e => (object?)e.Name).ToArray();
        var customMetrics = suite.Metrics
            .Select(metric => metric.Name)
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var comparison in suite.Comparisons)
        {
            if (string.IsNullOrWhiteSpace(comparison.Baseline))
                throw new InvalidOperationException("Benchmark comparison baseline is required.");
            if (!string.Equals(comparison.Dimension, "Engine", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Benchmark comparison dimension '{comparison.Dimension}' is not supported. Only Engine comparisons are supported.");
            if (!engineValues.Any(value => string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), comparison.Baseline, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Benchmark comparison baseline engine '{comparison.Baseline}' is not declared by suite '{suite.Name}'.");
            foreach (var metric in GetComparisonMetrics(comparison))
            {
                var normalized = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric.Trim();
                if (comparison.RequireBaselineFastest && !BenchmarkComparisonSemantics.IsDurationMetric(normalized))
                    throw new NotSupportedException($"Benchmark comparison metric '{metric}' cannot require the baseline to be fastest because only duration metrics have lower-is-better semantics.");
                if (IsPrimaryComparisonMetric(normalized) || customMetrics.Contains(normalized))
                    continue;
                throw new NotSupportedException($"Benchmark comparison metric '{metric}' is not supported by suite '{suite.Name}'. Use MedianMs, MeanMs, MinMs, MaxMs, P95Ms/P95, P99Ms/P99, StdDevMs/StdDev, StdErrMs/StdErr, or a declared custom metric.");
            }
        }
    }

    private static void ValidateCurrentRunspaceProfile(PowerShellBenchmarkSuite suite)
    {
        if (suite.Profile == PowerShellBenchmarkProfileKind.TemporaryLocalUser)
            throw new InvalidOperationException("Benchmark profile 'TemporaryLocalUser' requires a file-backed Invoke-BenchmarkSuite run so the benchmark spec can be re-evaluated inside the temporary Windows user profile.");
    }

    private static IEnumerable<string> GetComparisonMetrics(PowerShellBenchmarkComparison comparison)
        => comparison.Metrics is null || comparison.Metrics.Length == 0
            ? new[] { "MedianMs" }
            : comparison.Metrics;

    private static bool IsPrimaryComparisonMetric(string metric)
        => string.Equals(metric, "MedianMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "MeanMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "MinMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "MaxMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "P95Ms", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "P95", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "P99Ms", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "P99", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "StdDevMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "StdDev", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "StdErrMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(metric, "StdErr", StringComparison.OrdinalIgnoreCase);

    private static void ValidateComparisonWorkItems(PowerShellBenchmarkSuite suite, IReadOnlyList<PowerShellBenchmarkWorkItem> workItems)
    {
        foreach (var comparison in suite.Comparisons)
        {
            if (string.IsNullOrWhiteSpace(comparison.Baseline))
                continue;
            if (!string.Equals(comparison.Dimension, "Engine", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var group in workItems.GroupBy(item => ComparisonGroupKey(suite.Name, item), StringComparer.Ordinal))
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

    private static IEnumerable<PowerShellBenchmarkWorkItem> OrderWorkItems(IReadOnlyList<PowerShellBenchmarkWorkItem> items, int iteration, PowerShellBenchmarkRunOrder order)
        => order switch
        {
            PowerShellBenchmarkRunOrder.Sequential => items,
            PowerShellBenchmarkRunOrder.Randomized => Randomize(items, iteration),
            _ => Rotate(items, iteration)
        };

    private static IEnumerable<PowerShellBenchmarkWorkItem> Rotate(IReadOnlyList<PowerShellBenchmarkWorkItem> items, int iteration)
    {
        if (items.Count == 0)
            yield break;
        var offset = iteration % items.Count;
        for (var i = 0; i < items.Count; i++)
            yield return items[(offset + i) % items.Count];
    }

    private static IEnumerable<PowerShellBenchmarkWorkItem> Randomize(IReadOnlyList<PowerShellBenchmarkWorkItem> items, int iteration)
    {
        var ordered = items.ToArray();
        var random = new Random(unchecked((iteration + 1) * 397) ^ ordered.Length);
        for (var i = ordered.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return ordered;
    }

    private static void ApplyCooldown(PowerShellBenchmarkSuite suite)
    {
        if (suite.CooldownMilliseconds > 0)
            System.Threading.Thread.Sleep(suite.CooldownMilliseconds);
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
        if (value is double d) { number = d; return IsFinite(number); }
        if (value is float f) { number = f; return IsFinite(number); }
        if (value is decimal m) { number = (double)m; return true; }
        if (value is int i) { number = i; return true; }
        if (value is long l) { number = l; return true; }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
               && IsFinite(number);
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

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

    private static string MatrixPathSegment(IReadOnlyDictionary<string, object?> values)
        => PowerShellBenchmarkPathSegments.Matrix(values, IsBuiltInPathValue);

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
           || string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Profile", StringComparison.OrdinalIgnoreCase);

    private static PSObject ToPsObject(IReadOnlyDictionary<string, object?> values)
    {
        var expando = new System.Dynamic.ExpandoObject();
        var psObject = new PSObject(expando);
        var members = (IDictionary<string, object?>)expando;
        foreach (var entry in values)
            members[entry.Key] = entry.Value;
        return psObject;
    }

    private static void SetProperty(PSObject value, string name, object? propertyValue)
    {
        var existing = value.Properties[name];
        if (existing is null)
            value.Properties.Add(new PSNoteProperty(name, propertyValue));
        else
            existing.Value = propertyValue;
    }

    private static Collection<PSObject> InvokeStrict(ScriptBlock block, params object[] args)
    {
        var variables = new List<PSVariable>
        {
            new("ErrorActionPreference", ActionPreference.Stop),
            new("PSNativeCommandUseErrorActionPreference", true)
        };
        return NativeExitAwareInvokeWrapper.InvokeWithContext(functionsToDefine: null, variablesToDefine: variables, new object[] { PrepareNativeExitGuardedBlock(block), args, false, typeof(PowerShellNativeExitCodeTracker) });
    }

    private static ScriptBlock PrepareNativeExitGuardedBlock(ScriptBlock block)
        => block.Module is null
            ? ScriptBlock.Create(PowerShellNativeExitCodeGuard.AddChecks(block.ToString()))
            : block;

    private static readonly ScriptBlock NativeExitAwareInvokeWrapper =
        ScriptBlock.Create(EmbeddedScripts.Load("Scripts/Benchmarks/Invoke-NativeExitAwareBlock.ps1"));

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
           || string.Equals(key, "Profile", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "RunMode", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Iteration", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Status", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "DurationMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "Reason", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "SampleCount", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "FailureCount", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "OutlierCount", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "FailureReasons", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "MedianMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "MeanMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "MinMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "MaxMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "P95Ms", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "P99Ms", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "StdDevMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "StdErrMs", StringComparison.OrdinalIgnoreCase);

    internal static bool IsBenchmarkColumnName(string key)
        => IsBenchmarkColumn(key);

    private static readonly HashSet<string> ReservedMatrixAxisNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Scenario",
        "Suite",
        "RunId",
        "Iteration",
        "Status",
        "DurationMs",
        "Reason",
        "AllocatedBytes",
        "WorkingSetDeltaBytes",
        "OutputMetric",
        "SampleCount",
        "FailureCount",
        "MedianMs",
        "MeanMs",
        "MinMs",
        "MaxMs",
        "P95Ms",
        "P99Ms",
        "StdDevMs",
        "StdErrMs",
        "OutlierCount",
        "FailureReasons"
    };

    private static readonly HashSet<string> ReservedCaseVariableNames = CreateReservedCaseVariableNames();

    private static HashSet<string> CreateReservedCaseVariableNames()
    {
        var names = new HashSet<string>(ReservedMatrixAxisNames, StringComparer.OrdinalIgnoreCase)
        {
            "Scenario",
            "Engine",
            "Operation",
            "Host",
            "Profile",
            "OS",
            "RunMode"
        };
        return names;
    }

}
