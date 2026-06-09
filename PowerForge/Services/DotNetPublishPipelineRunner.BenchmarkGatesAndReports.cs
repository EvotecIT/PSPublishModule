using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class DotNetPublishBenchmarkExtractionResult
    {
        public string GateId { get; set; } = string.Empty;
        public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissingRequiredMetrics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Messages { get; } = new();
    }

    private void RunBenchmarkExtractStep(
        DotNetPublishPlan plan,
        IDictionary<string, DotNetPublishBenchmarkExtractionResult> extractedByGate,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (extractedByGate is null) throw new ArgumentNullException(nameof(extractedByGate));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var gateId = (step.GateId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(gateId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing GateId.");

        var gate = (plan.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGatePlan>())
            .FirstOrDefault(g => string.Equals(g.Id, gateId, StringComparison.OrdinalIgnoreCase));
        if (gate is null)
            throw new InvalidOperationException($"Benchmark gate '{gateId}' not found in plan.");
        if (!gate.Enabled)
        {
            extractedByGate[gateId] = new DotNetPublishBenchmarkExtractionResult { GateId = gateId };
            return;
        }

        if (!File.Exists(gate.SourcePath))
            throw new FileNotFoundException($"Benchmark source input not found for gate '{gateId}': {gate.SourcePath}", gate.SourcePath);

        var text = File.ReadAllText(gate.SourcePath);
        JsonDocument? json = null;
        var result = new DotNetPublishBenchmarkExtractionResult { GateId = gateId };

        foreach (var metric in gate.Metrics ?? Array.Empty<DotNetPublishBenchmarkMetricPlan>())
        {
            if (metric is null) continue;

            var values = metric.Source switch
            {
                DotNetPublishBenchmarkMetricSource.JsonPath => ExtractMetricValuesFromJson(metric, text, ref json),
                DotNetPublishBenchmarkMetricSource.Regex => ExtractMetricValuesFromRegex(metric, text),
                _ => Array.Empty<double>()
            };

            if (values.Length == 0)
            {
                if (metric.Required)
                {
                    var msg = $"Benchmark gate '{gateId}' metric '{metric.Name}' could not be extracted from '{gate.SourcePath}'.";
                    result.Messages.Add(msg);
                    result.MissingRequiredMetrics.Add(metric.Name);
                    HandlePolicy(gate.OnMissingMetric, msg);
                }

                continue;
            }

            result.Values[metric.Name] = AggregateMetricValues(metric.Aggregation, values);
        }

        json?.Dispose();
        extractedByGate[gateId] = result;
        _logger.Info($"Benchmark extract '{gateId}': {result.Values.Count} metric(s) extracted, {result.MissingRequiredMetrics.Count} required metric(s) missing.");
    }

    private DotNetPublishBenchmarkGateResult RunBenchmarkGateStep(
        DotNetPublishPlan plan,
        IReadOnlyDictionary<string, DotNetPublishBenchmarkExtractionResult> extractedByGate,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (extractedByGate is null) throw new ArgumentNullException(nameof(extractedByGate));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var gateId = (step.GateId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(gateId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing GateId.");

        var gate = (plan.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGatePlan>())
            .FirstOrDefault(g => string.Equals(g.Id, gateId, StringComparison.OrdinalIgnoreCase));
        if (gate is null)
            throw new InvalidOperationException($"Benchmark gate '{gateId}' not found in plan.");
        if (!gate.Enabled)
        {
            return new DotNetPublishBenchmarkGateResult
            {
                GateId = gateId,
                SourcePath = gate.SourcePath,
                BaselinePath = gate.BaselinePath,
                BaselineMode = gate.BaselineMode,
                Passed = true
            };
        }

        if (!extractedByGate.TryGetValue(gateId, out var extracted) || extracted is null)
            throw new InvalidOperationException($"Benchmark gate '{gateId}' has no extracted metrics. Ensure benchmark.extract step runs first.");

        var metricResults = new List<DotNetPublishBenchmarkMetricResult>();
        var messages = new List<string>();
        if (extracted.Messages.Count > 0)
            messages.AddRange(extracted.Messages);

        var failed = false;

        foreach (var missing in extracted.MissingRequiredMetrics)
        {
            metricResults.Add(new DotNetPublishBenchmarkMetricResult
            {
                Name = missing,
                MissingInSource = true
            });
            failed = true;
        }

        if (gate.BaselineMode == DotNetPublishBaselineMode.Update)
        {
            WriteBenchmarkBaseline(gate.BaselinePath, extracted.Values);
            foreach (var kv in extracted.Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                metricResults.Add(new DotNetPublishBenchmarkMetricResult
                {
                    Name = kv.Key,
                    Actual = kv.Value
                });
            }

            var updateResult = new DotNetPublishBenchmarkGateResult
            {
                GateId = gateId,
                SourcePath = gate.SourcePath,
                BaselinePath = gate.BaselinePath,
                BaselineMode = gate.BaselineMode,
                Passed = !failed,
                BaselineUpdated = true,
                RelativeTolerance = gate.RelativeTolerance,
                AbsoluteToleranceMs = gate.AbsoluteToleranceMs,
                Metrics = metricResults
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Messages = messages.ToArray()
            };

            if (!updateResult.Passed)
            {
                HandlePolicy(
                    gate.OnMissingMetric,
                    $"Benchmark gate '{gateId}' baseline update completed with missing required metric(s).");
            }

            _logger.Info($"Benchmark gate '{gateId}' updated baseline: {gate.BaselinePath}");
            return updateResult;
        }

        if (!File.Exists(gate.BaselinePath))
            throw new FileNotFoundException($"Benchmark baseline not found for gate '{gateId}': {gate.BaselinePath}", gate.BaselinePath);

        var baseline = ReadBenchmarkBaseline(gate.BaselinePath);
        foreach (var kv in extracted.Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var metric = new DotNetPublishBenchmarkMetricResult
            {
                Name = kv.Key,
                Actual = kv.Value
            };

            if (!baseline.TryGetValue(kv.Key, out var baselineValue))
            {
                metric.MissingInBaseline = true;
                var msg = $"Benchmark gate '{gateId}' metric '{kv.Key}' is missing in baseline '{gate.BaselinePath}'.";
                messages.Add(msg);
                if (gate.FailOnNew)
                    failed = true;
            }
            else
            {
                var allowed = GetAllowedMetricCap(
                    baselineValue,
                    gate.RelativeTolerance,
                    gate.AbsoluteToleranceMs);
                metric.Baseline = baselineValue;
                metric.Allowed = allowed;
                metric.Regressed = kv.Value > allowed;
                if (metric.Regressed)
                {
                    failed = true;
                    messages.Add(
                        $"Benchmark gate '{gateId}' metric '{kv.Key}' regressed: actual={kv.Value:0.###}ms, allowed={allowed:0.###}ms, baseline={baselineValue:0.###}ms.");
                }
            }

            metricResults.Add(metric);
        }

        var gateResult = new DotNetPublishBenchmarkGateResult
        {
            GateId = gateId,
            SourcePath = gate.SourcePath,
            BaselinePath = gate.BaselinePath,
            BaselineMode = gate.BaselineMode,
            Passed = !failed,
            BaselineUpdated = false,
            RelativeTolerance = gate.RelativeTolerance,
            AbsoluteToleranceMs = gate.AbsoluteToleranceMs,
            Metrics = metricResults
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Messages = messages.ToArray()
        };

        if (failed)
            HandlePolicy(gate.OnRegression, $"Benchmark gate '{gateId}' failed. {messages.FirstOrDefault() ?? "See gate result details."}");

        _logger.Info($"Benchmark gate '{gateId}': {(gateResult.Passed ? "passed" : "failed")} ({gateResult.Metrics.Length} metric(s)).");
        return gateResult;
    }

    private string? TryWriteRunReport(
        DotNetPublishPlan plan,
        DotNetPublishResult result,
        IReadOnlyList<DotNetPublishRunReportStep> steps,
        DateTimeOffset runStartedUtc,
        TimeSpan runDuration)
    {
        if (plan is null) return null;
        if (result is null) return null;
        if (string.IsNullOrWhiteSpace(plan.Outputs.RunReportPath)) return null;

        var reportPath = plan.Outputs.RunReportPath!;
        try
        {
            if (!plan.AllowManifestOutsideProjectRoot)
                EnsurePathWithinRoot(plan.ProjectRoot, reportPath, "RunReportPath");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);

            var report = new DotNetPublishRunReport
            {
                StartedUtc = runStartedUtc,
                FinishedUtc = runStartedUtc + runDuration,
                DurationMs = (long)Math.Max(0, runDuration.TotalMilliseconds),
                Succeeded = result.Succeeded,
                ErrorMessage = result.ErrorMessage,
                Steps = (steps ?? Array.Empty<DotNetPublishRunReportStep>()).ToArray(),
                Artefacts = new DotNetPublishRunReportArtefacts
                {
                    PublishCount = result.Artefacts?.Length ?? 0,
                    MsiPrepareCount = result.MsiPrepares?.Length ?? 0,
                    MsiBuildCount = result.MsiBuilds?.Length ?? 0,
                    TotalPublishBytes = result.Artefacts?.Sum(a => a.TotalBytes) ?? 0
                },
                Signing = new DotNetPublishRunReportSigning
                {
                    PublishFilesSigned = result.Artefacts?.Sum(a => a.SignedFiles) ?? 0,
                    MsiFilesSigned = result.MsiBuilds?.Sum(m => m.SignedFiles?.Length ?? 0) ?? 0
                },
                Gates = result.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGateResult>()
            };

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(reportPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return reportPath;
        }
        catch
        {
            _logger.Warn($"Failed to write run report: {reportPath}");
            return null;
        }
    }

    private static Dictionary<string, double> ReadBenchmarkBaseline(string path)
    {
        using var stream = File.OpenRead(path);
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Benchmark baseline must be a JSON object: {path}");

        JsonElement metricsNode;
        if (root.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object)
            metricsNode = metrics;
        else
            metricsNode = root;

        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in metricsNode.EnumerateObject())
        {
            if (TryConvertToDouble(prop.Value, out var value))
                map[prop.Name] = value;
        }

        return map;
    }

    private static void WriteBenchmarkBaseline(string path, IReadOnlyDictionary<string, double> metrics)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var ordered = (metrics ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        var payload = new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            metrics = ordered
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static double[] ExtractMetricValuesFromJson(
        DotNetPublishBenchmarkMetricPlan metric,
        string text,
        ref JsonDocument? json)
    {
        if (string.IsNullOrWhiteSpace(metric.Path))
            return Array.Empty<double>();

        json ??= JsonDocument.Parse(text);
        if (!TryResolveJsonPath(json.RootElement, metric.Path!, out var element))
            return Array.Empty<double>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            var values = new List<double>();
            foreach (var item in element.EnumerateArray())
            {
                if (TryConvertToDouble(item, out var value))
                    values.Add(value);
            }

            return values.ToArray();
        }

        if (TryConvertToDouble(element, out var single))
            return new[] { single };

        return Array.Empty<double>();
    }

    private static double[] ExtractMetricValuesFromRegex(DotNetPublishBenchmarkMetricPlan metric, string text)
    {
        if (string.IsNullOrWhiteSpace(metric.Pattern))
            return Array.Empty<double>();

        var values = new List<double>();
        var regex = new Regex(
            metric.Pattern!,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

        foreach (Match match in regex.Matches(text ?? string.Empty))
        {
            if (!match.Success) continue;
            if (match.Groups.Count <= metric.Group) continue;
            var raw = match.Groups[metric.Group].Value;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (TryConvertToDouble(raw, out var value))
                values.Add(value);
        }

        return values.ToArray();
    }

    private static double AggregateMetricValues(DotNetPublishBenchmarkMetricAggregation aggregation, IReadOnlyList<double> values)
    {
        if (values is null || values.Count == 0) return double.NaN;

        return aggregation switch
        {
            DotNetPublishBenchmarkMetricAggregation.First => values[0],
            DotNetPublishBenchmarkMetricAggregation.Min => values.Min(),
            DotNetPublishBenchmarkMetricAggregation.Max => values.Max(),
            DotNetPublishBenchmarkMetricAggregation.Average => values.Average(),
            DotNetPublishBenchmarkMetricAggregation.Last => values[values.Count - 1],
            _ => values[values.Count - 1]
        };
    }

    private static double GetAllowedMetricCap(double baseline, double relativeTolerance, double absoluteToleranceMs)
    {
        var relativeCap = baseline * (1.0 + Math.Max(0, relativeTolerance));
        var absoluteCap = baseline + Math.Max(0, absoluteToleranceMs);
        return Math.Max(relativeCap, absoluteCap);
    }

    private static bool TryResolveJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        var segments = (path ?? string.Empty)
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;

        foreach (var rawSegment in segments)
        {
            var segment = (rawSegment ?? string.Empty).Trim();
            if (segment.Length == 0) return false;

            var indexStart = segment.IndexOf('[');
            var propertyName = indexStart >= 0 ? segment.Substring(0, indexStart) : segment;
            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                if (!TryGetPropertyIgnoreCase(value, propertyName, out var child))
                    return false;
                value = child;
            }

            while (indexStart >= 0)
            {
                var indexEnd = segment.IndexOf(']', indexStart + 1);
                if (indexEnd <= indexStart) return false;

                var indexToken = segment.Substring(indexStart + 1, indexEnd - indexStart - 1).Trim();
                if (!int.TryParse(indexToken, out var index) || index < 0) return false;

                if (value.ValueKind != JsonValueKind.Array) return false;
                if (index >= value.GetArrayLength()) return false;
                value = value[index];

                indexStart = segment.IndexOf('[', indexEnd + 1);
            }
        }

        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        value = default;
        if (node.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in node.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertToDouble(JsonElement value, out double parsed)
    {
        parsed = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDouble(out parsed);
            case JsonValueKind.String:
                return TryConvertToDouble(value.GetString(), out parsed);
            default:
                return false;
        }
    }

    private static bool TryConvertToDouble(string? raw, out double parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
            return true;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed))
            return true;

        var normalized = raw!.Replace(',', '.');
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed);
    }
}
