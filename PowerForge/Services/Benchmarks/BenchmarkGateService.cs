using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Evaluates normalized benchmark summaries against JSON baselines.
/// </summary>
public sealed class BenchmarkGateService
{
    /// <summary>
    /// Evaluates or updates a benchmark baseline.
    /// </summary>
    /// <param name="request">Gate request.</param>
    /// <returns>Gate result.</returns>
    public BenchmarkGateResult Evaluate(BenchmarkGateRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var rows = BenchmarkJson.ReadSummary(request.SummaryPath);
        var actual = BuildMetricMap(rows, request);
        var failedRowMessages = BuildFailedRowMessages(rows, request).ToArray();
        if (request.BaselineMode == BenchmarkBaselineMode.Update)
        {
            if (failedRowMessages.Length > 0)
            {
                return new BenchmarkGateResult
                {
                    Passed = false,
                    BaselineUpdated = false,
                    BaselinePath = Path.GetFullPath(request.BaselinePath),
                    Messages = failedRowMessages
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
        var failed = failedRowMessages.Length > 0;
        messages.AddRange(failedRowMessages);
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
                var allowed = Math.Max(
                    baselineValue * (1.0 + Math.Max(0, request.RelativeTolerance)),
                    baselineValue + Math.Max(0, request.AbsoluteToleranceMs));
                result.Baseline = baselineValue;
                result.Allowed = allowed;
                result.Regressed = entry.Value > allowed;
                if (result.Regressed)
                {
                    failed = true;
                    messages.Add(
                        $"Benchmark metric '{entry.Key}' regressed: actual={entry.Value.ToString("0.###", CultureInfo.InvariantCulture)}, allowed={allowed.ToString("0.###", CultureInfo.InvariantCulture)}, baseline={baselineValue.ToString("0.###", CultureInfo.InvariantCulture)}.");
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
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows ?? Array.Empty<BenchmarkSummaryRow>())
        {
            var metric = NormalizeMetricName(request.Metric);
            var value = BenchmarkSummaryService.GetMetricValue(row, metric);
            if (!value.HasValue) continue;
            map[BuildKey(row, request.GroupBy, metric)] = value.Value;
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

    private static bool IsFailedRow(BenchmarkSummaryRow row)
        => row.FailureCount > 0 || string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase);

    private static string BuildKey(BenchmarkSummaryRow row, IReadOnlyList<string> groupBy, string metric)
    {
        var values = new List<string>();
        foreach (var field in groupBy.Count == 0 ? new[] { "Suite", "Scenario", "Operation", "Engine", "Host", "Variables" } : groupBy)
        {
            values.Add(GetField(row, field));
        }

        values.Add(NormalizeMetricName(metric));
        return string.Join("|", values.Select(v => v.Replace("|", "\\|")));
    }

    private static string GetField(BenchmarkSummaryRow row, string field)
    {
        if (string.Equals(field, "Suite", StringComparison.OrdinalIgnoreCase)) return row.Suite;
        if (string.Equals(field, "Scenario", StringComparison.OrdinalIgnoreCase)) return row.Scenario;
        if (string.Equals(field, "Operation", StringComparison.OrdinalIgnoreCase)) return row.Operation;
        if (string.Equals(field, "Engine", StringComparison.OrdinalIgnoreCase)) return row.Engine;
        if (string.Equals(field, "Host", StringComparison.OrdinalIgnoreCase)) return row.Host;
        if (string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase)) return row.Status;
        if (string.Equals(field, "Variables", StringComparison.OrdinalIgnoreCase)) return FormatVariables(row.Variables);
        const string variablePrefix = "Variables.";
        if (field.StartsWith(variablePrefix, StringComparison.OrdinalIgnoreCase)
            && row.Variables.TryGetValue(field.Substring(variablePrefix.Length), out var value))
            return value ?? string.Empty;
        return string.Empty;
    }

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            ";",
            (variables ?? new Dictionary<string, string?>())
                .Where(k => !string.Equals(k.Key, "Engine", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(k.Key, "Operation", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(k.Key, "Host", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));

    private static string NormalizeMetricName(string? metric)
    {
        var name = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric!.Trim();
        if (string.Equals(name, "MedianMs", StringComparison.OrdinalIgnoreCase)) return "MedianMs";
        if (string.Equals(name, "MeanMs", StringComparison.OrdinalIgnoreCase)) return "MeanMs";
        if (string.Equals(name, "MinMs", StringComparison.OrdinalIgnoreCase)) return "MinMs";
        if (string.Equals(name, "MaxMs", StringComparison.OrdinalIgnoreCase)) return "MaxMs";
        return name;
    }

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
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var value))
                map[prop.Name] = value;
        }

        return map;
    }

    private static void WriteBaseline(string path, IReadOnlyDictionary<string, double> metrics)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var ordered = metrics.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
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
}
