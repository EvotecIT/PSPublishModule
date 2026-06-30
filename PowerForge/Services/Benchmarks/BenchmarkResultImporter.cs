using System.Globalization;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Imports existing benchmark artifacts into the common benchmark schema.
/// </summary>
public sealed class BenchmarkResultImporter
{
    /// <summary>
    /// Imports a file or directory of benchmark artifacts.
    /// </summary>
    /// <param name="path">Input file or directory path.</param>
    /// <param name="suite">Optional suite name override.</param>
    /// <returns>Imported run result.</returns>
    public BenchmarkRunResult Import(string path, string? suite = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Input path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            return ImportDirectory(fullPath, suite);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Benchmark input was not found: {path}", path);

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ImportJson(fullPath, suite);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return ImportCsv(fullPath, suite);

        throw new NotSupportedException($"Unsupported benchmark input extension: {extension}");
    }

    private BenchmarkRunResult ImportDirectory(string path, string? suite)
    {
        var files = Directory.GetFiles(path, "*-report.csv", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(path, "*.csv", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException($"No benchmark CSV files were found under '{path}'.");

        var samples = new List<BenchmarkSample>();
        foreach (var file in files)
            samples.AddRange(ImportCsvSamples(file, suite ?? new DirectoryInfo(path).Name));

        return BuildImportedResult(suite ?? new DirectoryInfo(path).Name, samples);
    }

    private BenchmarkRunResult ImportJson(string path, string? suite)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && BenchmarkJson.TryGetPropertyIgnoreCase(root, "samples", out var samplesNode))
        {
            var result = BenchmarkJson.Read<BenchmarkRunResult>(path);
            if (!string.IsNullOrWhiteSpace(suite))
                ApplySuiteOverride(result, suite!);
            else if (result.Summary.Length == 0)
                result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);
            return result;
        }

        if (root.ValueKind == JsonValueKind.Array || (root.ValueKind == JsonValueKind.Object && BenchmarkJson.TryGetPropertyIgnoreCase(root, "summary", out _)))
        {
            var summary = BenchmarkJson.ReadSummary(path);
            if (!string.IsNullOrWhiteSpace(suite))
            {
                foreach (var row in summary)
                    row.Suite = suite!;
            }

            return new BenchmarkRunResult
            {
                RunId = "import-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                Suite = suite ?? summary.FirstOrDefault()?.Suite ?? Path.GetFileNameWithoutExtension(path),
                StartedUtc = DateTimeOffset.UtcNow,
                FinishedUtc = DateTimeOffset.UtcNow,
                Summary = summary
            };
        }

        if (root.ValueKind == JsonValueKind.Object && TryImportBenchmarkDotNetJson(root, path, suite, out var imported))
            return imported;

        throw new InvalidOperationException($"Unsupported benchmark JSON shape: {path}");
    }

    private BenchmarkRunResult ImportCsv(string path, string? suite)
    {
        var samples = ImportCsvSamples(path, suite ?? Path.GetFileNameWithoutExtension(path));
        return BuildImportedResult(suite ?? Path.GetFileNameWithoutExtension(path), samples);
    }

    private static BenchmarkRunResult BuildImportedResult(string suite, IReadOnlyList<BenchmarkSample> samples)
    {
        var summarizer = new BenchmarkSummaryService();
        var now = DateTimeOffset.UtcNow;
        return new BenchmarkRunResult
        {
            RunId = "import-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            Suite = suite,
            StartedUtc = now,
            FinishedUtc = now,
            Samples = samples.ToArray(),
            Summary = summarizer.Summarize(samples),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["importedUtc"] = now.ToString("O", CultureInfo.InvariantCulture)
            }
        };
    }

    private static BenchmarkSample[] ImportCsvSamples(string path, string suite)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return Array.Empty<BenchmarkSample>();
        var headers = ParseCsvLine(lines[0]);
        var samples = new List<BenchmarkSample>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i]);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var h = 0; h < headers.Length && h < values.Length; h++)
                map[headers[h]] = values[h];

            var method = Get(map, "Method", "Scenario", "Benchmark") ?? Path.GetFileNameWithoutExtension(path);
            var mean = ParseDuration(GetWithHeader(map, out var durationHeader, "MedianMs", "MeanMs", "DurationMs", "Median", "Mean", "Mean [ns]", "Mean [us]", "Mean [ms]"), durationHeader);
            samples.Add(new BenchmarkSample
            {
                RunId = "import",
                Suite = suite,
                Scenario = method,
                Operation = Get(map, "Operation") ?? "Run",
                Engine = Get(map, "Engine") ?? Get(map, "Job") ?? "BenchmarkDotNet",
                Host = Get(map, "Host") ?? string.Empty,
                Os = Get(map, "OS") ?? string.Empty,
                RunMode = "import",
                Iteration = 0,
                Status = mean.HasValue ? BenchmarkSampleStatus.Succeeded : BenchmarkSampleStatus.Failed,
                DurationMs = mean ?? 0,
                Reason = mean.HasValue ? string.Empty : "Duration column could not be parsed.",
                Variables = map.ToDictionary(k => k.Key, k => (string?)k.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        return samples.ToArray();
    }

    private static void ApplySuiteOverride(BenchmarkRunResult result, string suite)
    {
        result.Suite = suite;
        foreach (var sample in result.Samples)
            sample.Suite = suite;
        result.Summary = result.Samples.Length > 0
            ? new BenchmarkSummaryService().Summarize(result.Samples)
            : result.Summary.Select(row =>
            {
                row.Suite = suite;
                return row;
            }).ToArray();
        foreach (var row in result.Comparison)
            row.Suite = suite;
    }

    private static bool TryImportBenchmarkDotNetJson(JsonElement root, string path, string? suite, out BenchmarkRunResult result)
    {
        result = new BenchmarkRunResult();
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(root, "Benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
            return false;

        var samples = new List<BenchmarkSample>();
        foreach (var benchmark in benchmarks.EnumerateArray())
        {
            if (benchmark.ValueKind != JsonValueKind.Object)
                continue;

            var method = GetString(benchmark, "DisplayInfo")
                         ?? GetString(benchmark, "FullName")
                         ?? GetString(benchmark, "Method")
                         ?? GetString(benchmark, "MethodTitle")
                         ?? Path.GetFileNameWithoutExtension(path);
            var statistics = TryGetObject(benchmark, "Statistics");
            var mean = GetDouble(statistics, "Mean") ?? GetDouble(statistics, "Median");
            if (mean.HasValue && LooksLikeNanoseconds(statistics))
                mean *= 0.000001;

            var parameterText = GetString(benchmark, "Parameters") ?? string.Empty;
            var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(parameterText))
                variables["Parameters"] = parameterText;

            samples.Add(new BenchmarkSample
            {
                RunId = "import",
                Suite = suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path),
                Scenario = method,
                Operation = "Run",
                Engine = "BenchmarkDotNet",
                Host = GetString(root, "HostEnvironmentInfo") ?? string.Empty,
                Os = string.Empty,
                RunMode = "import",
                Iteration = 0,
                Status = mean.HasValue ? BenchmarkSampleStatus.Succeeded : BenchmarkSampleStatus.Failed,
                DurationMs = mean ?? 0,
                Reason = mean.HasValue ? string.Empty : "BenchmarkDotNet JSON duration could not be parsed.",
                Variables = variables
            });
        }

        if (samples.Count == 0)
            return false;

        result = BuildImportedResult(suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path), samples);
        return true;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? GetWithHeader(IReadOnlyDictionary<string, string> values, out string? matchedHeader, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                matchedHeader = name;
                return value.Trim();
            }
        }

        matchedHeader = null;
        return null;
    }

    private static JsonElement? TryGetObject(JsonElement node, string propertyName)
    {
        return BenchmarkJson.TryGetPropertyIgnoreCase(node, propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static string? GetString(JsonElement node, string propertyName)
    {
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(node, propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            return value.ToString();
        return null;
    }

    private static double? GetDouble(JsonElement? node, string propertyName)
    {
        if (!node.HasValue || !BenchmarkJson.TryGetPropertyIgnoreCase(node.Value, propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static bool LooksLikeNanoseconds(JsonElement? statistics)
    {
        var mean = GetDouble(statistics, "Mean");
        var median = GetDouble(statistics, "Median");
        return (mean.HasValue && mean.Value > 1000) || (median.HasValue && median.Value > 1000);
    }

    private static double? ParseDuration(string? raw, string? header = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw!.Trim().Replace(",", string.Empty);
        var factor = DurationFactor(text);
        if (Math.Abs(factor - 1.0) < double.Epsilon && !HasDurationSuffix(text))
            factor = DurationFactor(header);

        text = RemoveUnitSuffix(text).Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value * factor
            : null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var source = line ?? string.Empty;
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < source.Length && source[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static string RemoveUnitSuffix(string text)
    {
        foreach (var suffix in new[] { " ms", " ns", " us", " μs", " s", "ms", "ns", "us", "μs", "s" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text.Substring(0, text.Length - suffix.Length);
        }

        return text;
    }

    private static double DurationFactor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1.0;
        var trimmed = text!.Trim();
        if (trimmed.EndsWith("[ms]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" ms", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (trimmed.EndsWith("[ns]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" ns", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("ns", StringComparison.OrdinalIgnoreCase)) return 0.000001;
        if (trimmed.EndsWith("[us]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("[μs]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" us", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" μs", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("us", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("μs", StringComparison.OrdinalIgnoreCase)) return 0.001;
        if (trimmed.EndsWith("[s]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" s", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return 1000;
        return 1.0;
    }

    private static bool HasDurationSuffix(string text)
        => RemoveUnitSuffix(text).Length != text.Length;
}
