using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Evaluates normalized benchmark summaries against JSON baselines.
/// </summary>
public sealed class BenchmarkGateService
{
    private static readonly string[] DefaultGroupBy = { "Suite", "Scenario", "Operation", "Engine", "Host", "OS", "Variables" };

    /// <summary>
    /// Evaluates or updates a benchmark baseline.
    /// </summary>
    /// <param name="request">Gate request.</param>
    /// <returns>Gate result.</returns>
    public BenchmarkGateResult Evaluate(BenchmarkGateRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var rows = BenchmarkJson.ReadSummary(request.SummaryPath);
        ValidateGroupBy(request.GroupBy);
        var actual = BuildMetricMap(rows, request);
        var failedRowMessages = BuildFailedRowMessages(rows, request).ToArray();
        var missingMetricMessages = BuildMissingMetricMessages(rows, request).ToArray();
        var missingMetricMessage = actual.Count == 0
            ? $"No benchmark metric values were produced for metric '{NormalizeMetricName(request.Metric)}'."
            : null;
        if (request.BaselineMode == BenchmarkBaselineMode.Update)
        {
            if (failedRowMessages.Length > 0 || missingMetricMessages.Length > 0 || missingMetricMessage is not null)
            {
                var updateMessages = failedRowMessages.Concat(missingMetricMessages);
                if (missingMetricMessage is not null)
                    updateMessages = updateMessages.Concat(new[] { missingMetricMessage });
                return new BenchmarkGateResult
                {
                    Passed = false,
                    BaselineUpdated = false,
                    BaselinePath = Path.GetFullPath(request.BaselinePath),
                    Messages = updateMessages.ToArray()
                };
            }

            WriteBaseline(request.BaselinePath, actual);
            return new BenchmarkGateResult
            {
                Passed = true,
                BaselineUpdated = true,
                BaselinePath = Path.GetFullPath(request.BaselinePath),
                Metrics = actual.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(k => new BenchmarkGateMetricResult { Key = k.Key, Actual = k.Value })
                    .ToArray()
            };
        }

        if (!File.Exists(request.BaselinePath))
            throw new FileNotFoundException($"Benchmark baseline was not found: {request.BaselinePath}", request.BaselinePath);

        var baseline = ReadBaseline(request.BaselinePath);
        var metrics = new List<BenchmarkGateMetricResult>();
        var messages = new List<string>();
        var failed = failedRowMessages.Length > 0 || missingMetricMessages.Length > 0 || missingMetricMessage is not null;
        messages.AddRange(failedRowMessages);
        messages.AddRange(missingMetricMessages);
        if (missingMetricMessage is not null)
            messages.Add(missingMetricMessage);
        foreach (var entry in actual.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var result = new BenchmarkGateMetricResult
            {
                Key = entry.Key,
                Actual = entry.Value
            };

            if (!baseline.TryGetValue(entry.Key, out var baselineValue))
            {
                result.MissingInBaseline = true;
                messages.Add($"Benchmark metric '{entry.Key}' is missing from baseline.");
                failed |= request.FailOnNew;
            }
            else
            {
                var direction = ResolveMetricDirection(request);
                var allowed = GetAllowedLimit(
                    baselineValue,
                    request.RelativeTolerance,
                    request.AbsoluteToleranceMs,
                    direction);
                result.Baseline = baselineValue;
                result.Allowed = allowed;
                result.Regressed = IsRegressed(entry.Value, allowed, direction);
                if (result.Regressed)
                {
                    failed = true;
                    messages.Add(
                        $"Benchmark metric '{entry.Key}' regressed: actual={entry.Value.ToString("0.###", CultureInfo.InvariantCulture)}, allowed={allowed.ToString("0.###", CultureInfo.InvariantCulture)}, baseline={baselineValue.ToString("0.###", CultureInfo.InvariantCulture)}, direction={direction}.");
                }
            }

            metrics.Add(result);
        }

        foreach (var entry in baseline.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (actual.ContainsKey(entry.Key))
                continue;

            failed = true;
            metrics.Add(new BenchmarkGateMetricResult
            {
                Key = entry.Key,
                Actual = null,
                Baseline = entry.Value,
                MissingInCurrent = true
            });
            messages.Add($"Benchmark metric '{entry.Key}' is missing from the current run.");
        }

        return new BenchmarkGateResult
        {
            Passed = !failed,
            BaselineUpdated = false,
            BaselinePath = Path.GetFullPath(request.BaselinePath),
            Metrics = metrics.ToArray(),
            Messages = messages.ToArray()
        };
    }

    private static Dictionary<string, double> BuildMetricMap(IEnumerable<BenchmarkSummaryRow> rows, BenchmarkGateRequest request)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in rows ?? Array.Empty<BenchmarkSummaryRow>())
        {
            if (IsSkippedRow(row))
                continue;
            var metric = NormalizeMetricName(request.Metric);
            var value = BenchmarkSummaryService.GetMetricValue(row, metric);
            if (!value.HasValue || !IsFinite(value.Value)) continue;
            var key = BuildKey(row, request.GroupBy, metric);
            if (map.ContainsKey(key))
            {
                var fields = string.Join(", ", EffectiveGroupBy(request.GroupBy));
                throw new InvalidOperationException($"Benchmark gate GroupBy fields '{fields}' produced duplicate metric key '{key}'. Include enough fields to keep benchmark lanes distinct.");
            }

            map[key] = value.Value;
        }

        return map;
    }

    private static IEnumerable<string> BuildFailedRowMessages(IEnumerable<BenchmarkSummaryRow> rows, BenchmarkGateRequest request)
    {
        var metric = NormalizeMetricName(request.Metric);
        foreach (var row in rows ?? Array.Empty<BenchmarkSummaryRow>())
        {
            if (!IsFailedRow(row))
                continue;
            yield return $"Benchmark row '{BuildKey(row, request.GroupBy, metric)}' has failed samples.";
        }
    }

    private static IEnumerable<string> BuildMissingMetricMessages(IEnumerable<BenchmarkSummaryRow> rows, BenchmarkGateRequest request)
    {
        var metric = NormalizeMetricName(request.Metric);
        foreach (var row in rows ?? Array.Empty<BenchmarkSummaryRow>())
        {
            if (IsFailedRow(row) || IsSkippedRow(row))
                continue;
            var value = BenchmarkSummaryService.GetMetricValue(row, metric);
            if (value.HasValue && IsFinite(value.Value))
                continue;
            yield return $"Benchmark row '{BuildKey(row, request.GroupBy, metric)}' has no value for metric '{metric}'.";
        }
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFailedRow(BenchmarkSummaryRow row)
        => row.FailureCount > 0 || string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkippedRow(BenchmarkSummaryRow row)
        => string.Equals(row.Status, "Skipped", StringComparison.OrdinalIgnoreCase);

    private static string BuildKey(BenchmarkSummaryRow row, IReadOnlyList<string> groupBy, string metric)
    {
        var values = new List<string>();
        foreach (var field in EffectiveGroupBy(groupBy))
        {
            var value = GetField(row, field);
            values.Add(IsVariablesField(field) ? value : EscapeKeyComponent(value));
        }

        values.Add(EscapeKeyComponent(NormalizeMetricName(metric)));
        return string.Join("|", values);
    }

    private static IReadOnlyList<string> EffectiveGroupBy(IReadOnlyList<string>? groupBy)
        => groupBy is { Count: > 0 } ? groupBy : DefaultGroupBy;

    private static string GetField(BenchmarkSummaryRow row, string field)
    {
        if (string.Equals(field, "Suite", StringComparison.OrdinalIgnoreCase)) return row.Suite;
        if (string.Equals(field, "Scenario", StringComparison.OrdinalIgnoreCase)) return row.Scenario;
        if (string.Equals(field, "Operation", StringComparison.OrdinalIgnoreCase)) return row.Operation;
        if (string.Equals(field, "Engine", StringComparison.OrdinalIgnoreCase)) return row.Engine;
        if (string.Equals(field, "Host", StringComparison.OrdinalIgnoreCase)) return row.Host;
        if (string.Equals(field, "OS", StringComparison.OrdinalIgnoreCase) || string.Equals(field, "Os", StringComparison.OrdinalIgnoreCase)) return row.Os;
        if (string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase)) return row.Status;
        if (string.Equals(field, "Variables", StringComparison.OrdinalIgnoreCase)) return FormatVariables(row.Variables);
        const string variablePrefix = "Variables.";
        if (field.StartsWith(variablePrefix, StringComparison.OrdinalIgnoreCase)
            && TryGetVariable(row.Variables, field.Substring(variablePrefix.Length), out var value))
            return value ?? string.Empty;
        return string.Empty;
    }

    private static void ValidateGroupBy(IReadOnlyList<string> groupBy)
    {
        foreach (var rawField in groupBy ?? Array.Empty<string>())
        {
            var field = rawField?.Trim();
            if (string.IsNullOrWhiteSpace(field))
                throw new NotSupportedException("Benchmark gate group field is required.");
            if (IsSupportedGroupByField(field!))
                continue;
            throw new NotSupportedException($"Benchmark gate group field '{field}' is not supported. Use Suite, Scenario, Operation, Engine, Host, OS, Status, Variables, or Variables.<name>.");
        }
    }

    private static bool IsSupportedGroupByField(string field)
    {
        if (field.StartsWith("Variables.", StringComparison.OrdinalIgnoreCase))
            return field.Length > "Variables.".Length;
        return string.Equals(field, "Suite", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Scenario", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Operation", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Engine", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Host", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "OS", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Os", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase)
               || string.Equals(field, "Variables", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVariablesField(string field)
        => string.Equals(field, "Variables", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetVariable(IReadOnlyDictionary<string, string?> variables, string name, out string? value)
    {
        if (variables.TryGetValue(name, out value))
            return true;
        foreach (var entry in variables)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            ";",
            (variables ?? new Dictionary<string, string?>())
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(EscapeKeyComponent(k.Key), "=", EscapeKeyComponent(k.Value ?? string.Empty))));

    private static string EscapeKeyComponent(string? value)
    {
        var text = value ?? string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace(";", "\\;")
            .Replace("=", "\\=");
    }

    private static string NormalizeMetricName(string? metric)
    {
        var name = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric!.Trim();
        if (string.Equals(name, "MedianMs", StringComparison.OrdinalIgnoreCase)) return "MedianMs";
        if (string.Equals(name, "MeanMs", StringComparison.OrdinalIgnoreCase)) return "MeanMs";
        if (string.Equals(name, "MinMs", StringComparison.OrdinalIgnoreCase)) return "MinMs";
        if (string.Equals(name, "MaxMs", StringComparison.OrdinalIgnoreCase)) return "MaxMs";
        return name;
    }

    private static BenchmarkMetricDirection ResolveMetricDirection(BenchmarkGateRequest request)
    {
        if (request.MetricDirection != BenchmarkMetricDirection.Auto)
            return request.MetricDirection;

        var metric = NormalizeMetricName(request.Metric);
        if (metric.EndsWith("PerSecond", StringComparison.OrdinalIgnoreCase)
            || metric.EndsWith("PerSec", StringComparison.OrdinalIgnoreCase)
            || metric.IndexOf("/s", StringComparison.OrdinalIgnoreCase) >= 0
            || metric.IndexOf("Throughput", StringComparison.OrdinalIgnoreCase) >= 0
            || metric.IndexOf("Ops", StringComparison.OrdinalIgnoreCase) >= 0)
            return BenchmarkMetricDirection.HigherIsBetter;

        return BenchmarkMetricDirection.LowerIsBetter;
    }

    private static double GetAllowedLimit(
        double baselineValue,
        double relativeTolerance,
        double absoluteTolerance,
        BenchmarkMetricDirection direction)
    {
        var relative = Math.Max(0, relativeTolerance);
        var absolute = Math.Max(0, absoluteTolerance);
        return direction == BenchmarkMetricDirection.HigherIsBetter
            ? Math.Min(baselineValue * (1.0 - relative), baselineValue - absolute)
            : Math.Max(baselineValue * (1.0 + relative), baselineValue + absolute);
    }

    private static bool IsRegressed(double actual, double allowed, BenchmarkMetricDirection direction)
        => direction == BenchmarkMetricDirection.HigherIsBetter
            ? actual < allowed
            : actual > allowed;

    private static Dictionary<string, double> ReadBaseline(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Benchmark baseline must be a JSON object: {path}");

        var node = BenchmarkJson.TryGetPropertyIgnoreCase(root, "metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object
            ? metrics
            : root;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;
            if (!prop.Value.TryGetDouble(out var value) || !IsFinite(value))
                throw new InvalidOperationException($"Benchmark baseline metric '{prop.Name}' is not a finite number: {path}");
            map[prop.Name] = value;
        }

        return map;
    }

    private static void WriteBaseline(string path, IReadOnlyDictionary<string, double> metrics)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var ordered = metrics.OrderBy(k => k.Key, StringComparer.Ordinal)
            .ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        var payload = new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            metrics = ordered
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
