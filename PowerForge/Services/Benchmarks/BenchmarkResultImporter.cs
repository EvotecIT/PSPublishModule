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
        var defaultSuite = suite ?? new DirectoryInfo(path).Name;
        var runReport = Directory.GetFiles(path, "run-report.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (runReport is not null)
            return ImportJson(runReport, suite);

        var sampleFiles = Directory.GetFiles(path, "samples.csv", SearchOption.AllDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        if (sampleFiles.Length > 0)
        {
            var runnerSamples = ImportCsvSamples(sampleFiles[0], suite, defaultSuite);
            return BuildImportedResult(suite ?? runnerSamples.FirstOrDefault()?.Suite ?? defaultSuite, runnerSamples);
        }

        var benchmarkDotNetJsonFiles = Directory.GetFiles(path, "*-report*.json", SearchOption.AllDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsBenchmarkDotNetJsonReport)
            .GroupBy(BenchmarkDotNetJsonReportFamily, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(BenchmarkDotNetJsonReportPreference)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (benchmarkDotNetJsonFiles.Length > 0)
        {
            var benchmarkDotNetSamples = benchmarkDotNetJsonFiles.SelectMany(file => ImportJson(file, suite ?? defaultSuite).Samples).ToArray();
            return BuildImportedResult(suite ?? defaultSuite, benchmarkDotNetSamples);
        }

        var benchmarkDotNetFiles = Directory.GetFiles(path, "*-report.csv", SearchOption.AllDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (benchmarkDotNetFiles.Length > 0)
        {
            var benchmarkDotNetSamples = benchmarkDotNetFiles.SelectMany(file => ImportCsvSamples(file, suite, defaultSuite)).ToArray();
            return BuildImportedResult(suite ?? benchmarkDotNetSamples.FirstOrDefault()?.Suite ?? defaultSuite, benchmarkDotNetSamples);
        }

        var summaryFiles = Directory.GetFiles(path, "summary.csv", SearchOption.AllDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (summaryFiles.Length > 0)
        {
            var summaryRows = summaryFiles.SelectMany(file => ImportCsvSummary(file, suite, defaultSuite)).ToArray();
            return BuildImportedSummaryResult(suite ?? summaryRows.FirstOrDefault()?.Suite ?? defaultSuite, summaryRows);
        }

        var csvFiles = Directory.GetFiles(path, "*.csv", SearchOption.TopDirectoryOnly)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (csvFiles.Length == 0)
            throw new InvalidOperationException($"No benchmark CSV files were found under '{path}'.");

        var samples = csvFiles
            .Where(file => !LooksLikeSummaryCsv(file))
            .SelectMany(file => ImportCsvSamples(file, suite, defaultSuite))
            .ToArray();
        if (samples.Length > 0)
            return BuildImportedResult(suite ?? samples.FirstOrDefault()?.Suite ?? defaultSuite, samples);

        var summary = csvFiles.SelectMany(file => ImportCsvSummary(file, suite, defaultSuite)).ToArray();
        return BuildImportedSummaryResult(suite ?? summary.FirstOrDefault()?.Suite ?? defaultSuite, summary);
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

        if (root.ValueKind == JsonValueKind.Array && LooksLikeSampleArray(root))
        {
            var samples = BenchmarkJson.Read<BenchmarkSample[]>(path);
            if (!string.IsNullOrWhiteSpace(suite))
            {
                foreach (var sample in samples)
                    sample.Suite = suite!;
            }

            return BuildImportedResult(suite ?? samples.FirstOrDefault()?.Suite ?? Path.GetFileNameWithoutExtension(path), samples);
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
        var defaultSuite = suite ?? Path.GetFileNameWithoutExtension(path);
        if (LooksLikeSummaryCsv(path))
        {
            var summary = ImportCsvSummary(path, suite, defaultSuite);
            return BuildImportedSummaryResult(suite ?? summary.FirstOrDefault()?.Suite ?? defaultSuite, summary);
        }

        var samples = ImportCsvSamples(path, suite, defaultSuite);
        return BuildImportedResult(suite ?? samples.FirstOrDefault()?.Suite ?? defaultSuite, samples);
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

    private static BenchmarkRunResult BuildImportedSummaryResult(string suite, IReadOnlyList<BenchmarkSummaryRow> summary)
    {
        var now = DateTimeOffset.UtcNow;
        return new BenchmarkRunResult
        {
            RunId = "import-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            Suite = suite,
            StartedUtc = now,
            FinishedUtc = now,
            Summary = summary.ToArray(),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["importedUtc"] = now.ToString("O", CultureInfo.InvariantCulture)
            }
        };
    }

    private static BenchmarkSample[] ImportCsvSamples(string path, string? suiteOverride, string defaultSuite)
    {
        var records = ReadCsvRecords(path);
        if (records.Length < 2) return Array.Empty<BenchmarkSample>();
        var headers = records[0];
        var samples = new List<BenchmarkSample>();
        for (var i = 1; i < records.Length; i++)
        {
            var values = records[i];
            if (values.Length == 0 || values.All(string.IsNullOrWhiteSpace)) continue;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var h = 0; h < headers.Length && h < values.Length; h++)
                map[headers[h]] = values[h];

            var isBenchmarkDotNetCsv = LooksLikeBenchmarkDotNetCsv(headers);
            var metricHeaders = SampleMetricColumnsFor(headers, isBenchmarkDotNetCsv);
            var metadataColumns = SampleMetadataColumnsFor(map, isBenchmarkDotNetCsv);
            var method = GetCsvScenarioName(map, isBenchmarkDotNetCsv) ?? Path.GetFileNameWithoutExtension(path);
            var mean = ParseDuration(GetCsvSampleDuration(map, isBenchmarkDotNetCsv, out var durationHeader), durationHeader);
            var status = isBenchmarkDotNetCsv
                ? ParseSampleStatus(null, mean.HasValue)
                : ParseSampleStatus(Get(map, "Status"), mean.HasValue);
            samples.Add(new BenchmarkSample
            {
                RunId = "import",
                Suite = suiteOverride ?? Get(map, "Suite") ?? defaultSuite,
                Scenario = method,
                Operation = GetCsvOperation(map, isBenchmarkDotNetCsv),
                Engine = GetCsvEngine(map, isBenchmarkDotNetCsv),
                Host = GetCsvHost(map, isBenchmarkDotNetCsv),
                Os = Get(map, "OS") ?? string.Empty,
                RunMode = Get(map, "RunMode") ?? "import",
                Iteration = ParseInt(Get(map, "Iteration")) ?? 0,
                Status = status,
                DurationMs = mean ?? 0,
                AllocatedBytes = ParseLong(Get(map, "AllocatedBytes")),
                WorkingSetDeltaBytes = ParseLong(Get(map, "WorkingSetDeltaBytes")),
                OutputMetric = ParseNumericMetric(Get(map, "OutputMetric")),
                Reason = Get(map, "Reason") ?? (mean.HasValue ? string.Empty : "Duration column could not be parsed."),
                Variables = ExtractVariables(map, metadataColumns, metricHeaders, isBenchmarkDotNetCsv),
                Metrics = ExtractMetrics(map, metricHeaders, isBenchmarkDotNetCsv)
            });
        }

        return samples.ToArray();
    }

    private static BenchmarkSummaryRow[] ImportCsvSummary(string path, string? suiteOverride, string defaultSuite)
    {
        var records = ReadCsvRecords(path);
        if (records.Length < 2) return Array.Empty<BenchmarkSummaryRow>();
        var headers = records[0];
        var rows = new List<BenchmarkSummaryRow>();
        for (var i = 1; i < records.Length; i++)
        {
            var values = records[i];
            if (values.Length == 0 || values.All(string.IsNullOrWhiteSpace)) continue;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var h = 0; h < headers.Length && h < values.Length; h++)
                map[headers[h]] = values[h];

            var isBenchmarkDotNetCsv = LooksLikeBenchmarkDotNetCsv(headers);
            var metricHeaders = SummaryMetricColumnsFor(headers, isBenchmarkDotNetCsv);
            var metadataColumns = SummaryMetadataColumnsFor(map, isBenchmarkDotNetCsv);
            var failureCount = ParseInt(Get(map, "FailureCount")) ?? 0;
            rows.Add(new BenchmarkSummaryRow
            {
                Suite = suiteOverride ?? Get(map, "Suite") ?? defaultSuite,
                Scenario = GetCsvScenarioName(map, isBenchmarkDotNetCsv) ?? Path.GetFileNameWithoutExtension(path),
                Operation = GetCsvOperation(map, isBenchmarkDotNetCsv),
                Engine = GetCsvEngine(map, isBenchmarkDotNetCsv),
                Host = GetCsvHost(map, isBenchmarkDotNetCsv),
                Os = Get(map, "OS") ?? string.Empty,
                RunMode = Get(map, "RunMode") ?? string.Empty,
                Variables = ExtractVariables(map, metadataColumns, metricHeaders, isBenchmarkDotNetCsv),
                SampleCount = ParseInt(Get(map, "SampleCount")) ?? 0,
                FailureCount = failureCount,
                Status = Get(map, "Status") ?? (failureCount > 0 ? "Failed" : "Succeeded"),
                MedianMs = ParseDuration(GetWithHeader(map, out var medianHeader, "MedianMs", "Median [ns]", "Median [us]", "Median [ms]", "Median [s]", "Median"), medianHeader),
                MeanMs = ParseDuration(GetWithHeader(map, out var meanHeader, "MeanMs", "Mean [ns]", "Mean [us]", "Mean [ms]", "Mean [s]", "Mean"), meanHeader),
                MinMs = ParseDuration(GetWithHeader(map, out var minHeader, "MinMs", "Min [ns]", "Min [us]", "Min [ms]", "Min [s]", "Min"), minHeader),
                MaxMs = ParseDuration(GetWithHeader(map, out var maxHeader, "MaxMs", "Max [ns]", "Max [us]", "Max [ms]", "Max [s]", "Max"), maxHeader),
                Metrics = ExtractMetrics(map, metricHeaders, isBenchmarkDotNetCsv)
            });
        }

        return rows.ToArray();
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

            var method = GetString(benchmark, "Method")
                         ?? GetString(benchmark, "MethodTitle")
                         ?? GetString(benchmark, "FullName")
                         ?? GetString(benchmark, "DisplayInfo")
                         ?? Path.GetFileNameWithoutExtension(path);
            var statistics = TryGetObject(benchmark, "Statistics");
            var mean = GetDouble(statistics, "Median") ?? GetDouble(statistics, "Mean");
            if (mean.HasValue)
                mean *= 0.000001;

            var variables = ParseBenchmarkDotNetParameters(GetString(benchmark, "Parameters"));
            AddBenchmarkDotNetIdentityVariables(benchmark, variables, method);
            var engine = GetBenchmarkDotNetEngine(benchmark);
            var metrics = ExtractBenchmarkDotNetMetrics(statistics);
            AddBenchmarkDotNetMemoryMetrics(TryGetObject(benchmark, "Memory"), metrics);

            samples.Add(new BenchmarkSample
            {
                RunId = "import",
                Suite = suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path),
                Scenario = method,
                Operation = "Run",
                Engine = engine,
                Host = GetBenchmarkDotNetHost(root),
                Os = string.Empty,
                RunMode = "import",
                Iteration = 0,
                Status = mean.HasValue ? BenchmarkSampleStatus.Succeeded : BenchmarkSampleStatus.Failed,
                DurationMs = mean ?? 0,
                Reason = mean.HasValue ? string.Empty : "BenchmarkDotNet JSON duration could not be parsed.",
                Variables = variables,
                Metrics = metrics
            });
        }

        if (samples.Count == 0)
            return false;

        result = BuildImportedResult(suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path), samples);
        return true;
    }

    private static string BenchmarkDotNetJsonReportFamily(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var reportIndex = name.IndexOf("-report", StringComparison.OrdinalIgnoreCase);
        return reportIndex < 0
            ? Path.Combine(directory, name)
            : Path.Combine(directory, name.Substring(0, reportIndex) + "-report");
    }

    private static int BenchmarkDotNetJsonReportPreference(string path)
    {
        var name = Path.GetFileName(path);
        if (name.IndexOf("full-compressed", StringComparison.OrdinalIgnoreCase) >= 0) return 30;
        if (name.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return 20;
        if (name.EndsWith("-report.json", StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    private static bool IsBenchmarkDotNetJsonReport(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && BenchmarkJson.TryGetPropertyIgnoreCase(doc.RootElement, "Benchmarks", out var benchmarks)
                   && benchmarks.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetBenchmarkDotNetEngine(JsonElement benchmark)
    {
        var job = GetString(benchmark, "Job")
                  ?? GetString(benchmark, "JobDisplayInfo")
                  ?? GetString(benchmark, "JobId");
        if (!string.IsNullOrWhiteSpace(job))
            return job!;

        if (BenchmarkJson.TryGetPropertyIgnoreCase(benchmark, "Job", out var jobNode) && jobNode.ValueKind == JsonValueKind.Object)
        {
            var parts = new[]
            {
                GetString(jobNode, "DisplayInfo"),
                GetString(jobNode, "Id"),
                GetString(jobNode, "Runtime"),
                GetString(jobNode, "RuntimeMoniker"),
                GetString(jobNode, "Platform"),
                GetString(jobNode, "Jit")
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            if (parts.Length > 0)
                return string.Join("; ", parts);
        }

        return "BenchmarkDotNet";
    }

    private static string GetBenchmarkDotNetHost(JsonElement root)
    {
        var host = GetString(root, "HostEnvironmentInfo");
        if (!string.IsNullOrWhiteSpace(host))
            return host!;
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(root, "HostEnvironmentInfo", out var hostNode) || hostNode.ValueKind != JsonValueKind.Object)
            return string.Empty;

        var parts = new[]
        {
            GetString(hostNode, "BenchmarkDotNetCaption"),
            GetString(hostNode, "RuntimeVersion"),
            GetString(hostNode, "Runtime"),
            GetString(hostNode, "Jit"),
            GetString(hostNode, "Platform"),
            GetString(hostNode, "Architecture"),
            GetString(hostNode, "OperatingSystem")
        }
        .Where(part => !string.IsNullOrWhiteSpace(part))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
        return parts.Length == 0 ? hostNode.GetRawText() : string.Join("; ", parts);
    }

    private static Dictionary<string, string?> ParseBenchmarkDotNetParameters(string? parameterText)
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(parameterText))
            return variables;

        foreach (var segment in SplitBenchmarkDotNetParameterSegments(parameterText!))
        {
            var trimmed = segment.Trim();
            var separator = FindBenchmarkDotNetParameterSeparator(trimmed);
            if (separator <= 0 || separator >= trimmed.Length - 1)
                continue;
            var name = TrimBenchmarkDotNetParameterToken(trimmed.Substring(0, separator));
            var value = TrimBenchmarkDotNetParameterToken(trimmed.Substring(separator + 1));
            if (!string.IsNullOrWhiteSpace(name))
                variables[name] = value;
        }

        if (variables.Count == 0)
            variables["Parameters"] = parameterText;
        return variables;
    }

    private static IEnumerable<string> SplitBenchmarkDotNetParameterSegments(string text)
    {
        var start = 0;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote.HasValue)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (c == quote.Value)
                    quote = null;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c is not (',' or ';'))
                continue;

            if (i > start)
                yield return text.Substring(start, i - start);
            start = i + 1;
        }

        if (start < text.Length)
            yield return text.Substring(start);
    }

    private static int FindBenchmarkDotNetParameterSeparator(string text)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote.HasValue)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (c == quote.Value)
                    quote = null;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c is '=' or ':')
                return i;
        }

        return -1;
    }

    private static string TrimBenchmarkDotNetParameterToken(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static void AddBenchmarkDotNetIdentityVariables(
        JsonElement benchmark,
        IDictionary<string, string?> variables,
        string scenario)
    {
        var fullName = GetString(benchmark, "FullName");
        var type = GetString(benchmark, "Type") ?? GetString(benchmark, "TypeName");
        var ns = GetString(benchmark, "Namespace");

        if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(fullName))
            type = TryExtractBenchmarkDotNetType(fullName!, scenario);

        AddBenchmarkDotNetIdentityVariable(variables, "Namespace", ns);
        AddBenchmarkDotNetIdentityVariable(variables, "Type", type);
        AddBenchmarkDotNetIdentityVariable(variables, "FullName", fullName);
    }

    private static void AddBenchmarkDotNetIdentityVariable(IDictionary<string, string?> variables, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!variables.ContainsKey(name))
        {
            variables[name] = value;
            return;
        }

        var fallback = "BenchmarkDotNet" + name;
        if (!variables.ContainsKey(fallback))
            variables[fallback] = value;
    }

    private static string? TryExtractBenchmarkDotNetType(string fullName, string scenario)
    {
        var text = fullName.Trim();
        var parameterIndex = text.IndexOf('(');
        if (parameterIndex >= 0)
            text = text.Substring(0, parameterIndex);

        var suffix = "." + scenario;
        if (text.EndsWith(suffix, StringComparison.Ordinal))
            text = text.Substring(0, text.Length - suffix.Length);

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static Dictionary<string, double> ExtractBenchmarkDotNetMetrics(JsonElement? statistics)
    {
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!statistics.HasValue || statistics.Value.ValueKind != JsonValueKind.Object)
            return metrics;

        AddBenchmarkDotNetStatisticMetrics(statistics.Value, metrics);
        var percentiles = TryGetObject(statistics.Value, "Percentiles");
        if (percentiles.HasValue)
            AddBenchmarkDotNetStatisticMetrics(percentiles.Value, metrics);

        return metrics;
    }

    private static void AddBenchmarkDotNetStatisticMetrics(JsonElement statistics, IDictionary<string, double> metrics)
    {
        foreach (var property in statistics.EnumerateObject())
        {
            var metricName = BenchmarkDotNetStatisticMetricName(property.Name);
            if (metricName is null)
                continue;

            var value = GetDoubleValue(property.Value);
            if (!value.HasValue)
                continue;

            var scaled = value.Value * BenchmarkDotNetJsonMetricFactor(property.Name);
            metrics[metricName] = scaled;
            if (!string.Equals(metricName, property.Name, StringComparison.OrdinalIgnoreCase))
                metrics[property.Name] = scaled;
        }
    }

    private static void AddBenchmarkDotNetMemoryMetrics(JsonElement? memory, IDictionary<string, double> metrics)
    {
        if (!memory.HasValue || memory.Value.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in memory.Value.EnumerateObject())
        {
            var value = GetDouble(memory, property.Name);
            if (!value.HasValue)
                continue;

            metrics[property.Name] = value.Value;
            var alias = BenchmarkDotNetMemoryMetricAlias(property.Name);
            if (!string.IsNullOrWhiteSpace(alias))
                metrics[alias!] = value.Value;
        }
    }

    private static string? BenchmarkDotNetMemoryMetricAlias(string name)
    {
        var normalized = name.Replace(" ", string.Empty);
        if (string.Equals(normalized, "BytesAllocatedPerOperation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "AllocatedBytes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Allocated", StringComparison.OrdinalIgnoreCase))
            return "Allocated";
        if (string.Equals(normalized, "Gen0Collections", StringComparison.OrdinalIgnoreCase)) return "Gen0";
        if (string.Equals(normalized, "Gen1Collections", StringComparison.OrdinalIgnoreCase)) return "Gen1";
        if (string.Equals(normalized, "Gen2Collections", StringComparison.OrdinalIgnoreCase)) return "Gen2";
        return null;
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

    private static string? GetCsvScenarioName(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
        => isBenchmarkDotNetCsv
            ? Get(values, "Method", "Benchmark")
            : Get(values, "Scenario", "Method", "Benchmark");

    private static string GetCsvOperation(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
        => isBenchmarkDotNetCsv ? "Run" : Get(values, "Operation") ?? "Run";

    private static string GetCsvEngine(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
        => isBenchmarkDotNetCsv ? Get(values, "Job") ?? "BenchmarkDotNet" : Get(values, "Engine") ?? Get(values, "Job") ?? "BenchmarkDotNet";

    private static string GetCsvHost(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
        => isBenchmarkDotNetCsv ? string.Empty : Get(values, "Host") ?? string.Empty;

    private static string? GetCsvSampleDuration(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv, out string? matchedHeader)
        => isBenchmarkDotNetCsv
            ? GetWithHeader(values, out matchedHeader, "Median [ns]", "Median [us]", "Median [ms]", "Median [s]", "Median", "Mean [ns]", "Mean [us]", "Mean [ms]", "Mean [s]", "Mean", "MedianMs", "MeanMs", "DurationMs")
            : GetWithHeader(values, out matchedHeader, "DurationMs", "MedianMs", "MeanMs");

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
        return GetDoubleValue(value);
    }

    private static double? GetDoubleValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return IsFinite(number) ? number : null;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return IsFinite(number) ? number : null;
        return null;
    }

    private static double? ParseDuration(string? raw, string? header = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw!.Trim().Replace(",", string.Empty);
        var factor = DurationFactor(text);
        if (Math.Abs(factor - 1.0) < double.Epsilon && !HasDurationSuffix(text))
            factor = HeaderDurationFactor(header);

        text = RemoveUnitSuffix(text).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        var duration = value * factor;
        return IsFinite(duration) ? duration : null;
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double? ParseNumericMetric(string? raw, string? header = null)
        => ParseByteSize(raw) ?? ParseDuration(raw, header);

    private static double? ParseByteSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw!.Trim().Replace(",", string.Empty);
        foreach (var unit in ByteUnits)
        {
            if (!text.EndsWith(unit.Suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            var numberText = text.Substring(0, text.Length - unit.Suffix.Length).Trim();
            if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                var bytes = value * unit.Factor;
                return IsFinite(bytes) ? bytes : null;
            }
        }

        return null;
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

    private static string[][] ReadCsvRecords(string path)
    {
        var source = File.ReadAllText(path);
        var records = new List<string[]>();
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
            else if ((ch == '\r' || ch == '\n') && !quoted)
            {
                if (ch == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                values.Add(current.ToString());
                current.Clear();
                records.Add(values.ToArray());
                values.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0 || values.Count > 0)
        {
            values.Add(current.ToString());
            records.Add(values.ToArray());
        }

        if (records.Count > 0)
            records[0] = records[0].Select(NormalizeCsvHeader).ToArray();
        return records.ToArray();
    }

    private static string NormalizeCsvHeader(string header)
        => (header ?? string.Empty).Trim().TrimStart('\uFEFF');

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

    private static double HeaderDurationFactor(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return 1.0;
        var trimmed = header!.Trim();
        if (trimmed.EndsWith("[ms]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" ms", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("Ms", StringComparison.Ordinal)) return 1.0;
        if (trimmed.EndsWith("[ns]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" ns", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("Ns", StringComparison.Ordinal)) return 0.000001;
        if (trimmed.EndsWith("[us]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("[μs]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" us", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" μs", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("Us", StringComparison.Ordinal) || trimmed.EndsWith("μs", StringComparison.OrdinalIgnoreCase)) return 0.001;
        if (trimmed.EndsWith("[s]", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" s", StringComparison.OrdinalIgnoreCase)) return 1000;
        return 1.0;
    }

    private static bool HasDurationSuffix(string text)
        => RemoveUnitSuffix(text).Length != text.Length;

    private static bool LooksLikeSummaryCsv(string path)
    {
        var firstRecord = ReadCsvRecords(path).FirstOrDefault();
        if (firstRecord is null) return false;
        if (LooksLikeBenchmarkDotNetCsv(firstRecord))
            return false;
        var headers = new HashSet<string>(firstRecord, StringComparer.OrdinalIgnoreCase);
        return (headers.Contains("SampleCount") || headers.Contains("FailureCount") || headers.Contains("MedianMs"))
               && !headers.Contains("Iteration")
               && !headers.Contains("DurationMs");
    }

    private static bool LooksLikeSampleArray(JsonElement root)
    {
        var first = root.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object
               && (BenchmarkJson.TryGetPropertyIgnoreCase(first, "durationMs", out _)
                   || BenchmarkJson.TryGetPropertyIgnoreCase(first, "iteration", out _)
                   || BenchmarkJson.TryGetPropertyIgnoreCase(first, "runId", out _));
    }

    private static BenchmarkSampleStatus ParseSampleStatus(string? value, bool hasDuration)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<BenchmarkSampleStatus>(value, ignoreCase: true, out var status))
            return status == BenchmarkSampleStatus.Succeeded && !hasDuration
                ? BenchmarkSampleStatus.Failed
                : status;
        return hasDuration ? BenchmarkSampleStatus.Succeeded : BenchmarkSampleStatus.Failed;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static long? ParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static Dictionary<string, string?> ExtractVariables(
        IReadOnlyDictionary<string, string> values,
        HashSet<string> excludedColumns,
        HashSet<string>? metricColumns = null,
        bool isBenchmarkDotNetCsv = false)
        => values
            .Where(k => !IsExcludedVariableColumn(k.Key, k.Value, excludedColumns, metricColumns, isBenchmarkDotNetCsv))
            .ToDictionary(k => k.Key, k => (string?)k.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, double> ExtractMetrics(IReadOnlyDictionary<string, string> values, HashSet<string> metricColumns, bool normalizeBenchmarkDotNetMetrics)
    {
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in metricColumns.Where(values.ContainsKey))
        {
            var value = ParseNumericMetric(values[name], name);
            if (!value.HasValue)
                continue;

            var metricName = normalizeBenchmarkDotNetMetrics
                ? BenchmarkDotNetStatisticMetricName(name) ?? name
                : name;
            metrics[metricName] = value.Value;
        }

        return metrics;
    }

    private static HashSet<string> SampleMetricColumnsFor(string[] headers, bool includeBenchmarkDotNetStatisticColumns)
    {
        var metrics = HeadersAfter(headers, "Reason");
        if (includeBenchmarkDotNetStatisticColumns)
        {
            foreach (var header in headers.Where(IsBenchmarkDotNetMetricColumn))
                metrics.Add(header);
        }
        return metrics;
    }

    private static HashSet<string> SummaryMetricColumnsFor(string[] headers, bool includeBenchmarkDotNetStatisticColumns)
    {
        var metrics = HeadersAfter(headers, "MaxMs");
        if (includeBenchmarkDotNetStatisticColumns)
        {
            foreach (var header in headers.Where(IsBenchmarkDotNetMetricColumn))
                metrics.Add(header);
        }
        return metrics;
    }

    private static HashSet<string> HeadersAfter(string[] headers, string marker)
    {
        var index = Array.FindIndex(headers, header => string.Equals(header, marker, StringComparison.OrdinalIgnoreCase));
        return index < 0 || index + 1 >= headers.Length
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(headers.Skip(index + 1), StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> SampleMetadataColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "RunId", "Suite", "Scenario", "Method", "Benchmark", "Operation", "Engine", "Job", "Host", "OS", "RunMode",
        "Iteration", "Status", "DurationMs", "MedianMs", "MeanMs",
        "Reason", "AllocatedBytes", "WorkingSetDeltaBytes", "OutputMetric"
    };

    private static readonly HashSet<string> SummaryMetadataColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Suite", "Scenario", "Method", "Benchmark", "Operation", "Engine", "Job", "Host", "OS", "RunMode", "SampleCount", "FailureCount",
        "Status", "MedianMs", "MeanMs", "MinMs", "MaxMs"
    };

    private static HashSet<string> SampleMetadataColumnsFor(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
    {
        var columns = new HashSet<string>(SampleMetadataColumns, StringComparer.OrdinalIgnoreCase);
        if (isBenchmarkDotNetCsv)
        {
            columns.Remove("Scenario");
            columns.Remove("Operation");
            columns.Remove("Engine");
            columns.Remove("Host");
            columns.Remove("Iteration");
            columns.Remove("Status");
            columns.Remove("DurationMs");
            columns.Remove("MedianMs");
            columns.Remove("MeanMs");
            return columns;
        }

        if (HasText(values, "Scenario"))
        {
            columns.Remove("Method");
            columns.Remove("Benchmark");
            columns.Remove("Job");
        }
        return columns;
    }

    private static HashSet<string> SummaryMetadataColumnsFor(IReadOnlyDictionary<string, string> values, bool isBenchmarkDotNetCsv)
    {
        var columns = new HashSet<string>(SummaryMetadataColumns, StringComparer.OrdinalIgnoreCase);
        if (isBenchmarkDotNetCsv)
        {
            columns.Remove("Scenario");
            columns.Remove("Operation");
            columns.Remove("Engine");
            columns.Remove("Host");
            columns.Remove("DurationMs");
            columns.Remove("MedianMs");
            columns.Remove("MeanMs");
            columns.Remove("MinMs");
            columns.Remove("MaxMs");
            return columns;
        }

        if (HasText(values, "Scenario"))
        {
            columns.Remove("Method");
            columns.Remove("Benchmark");
            columns.Remove("Job");
        }
        return columns;
    }

    private static bool HasText(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool IsExcludedVariableColumn(
        string key,
        string value,
        HashSet<string> excludedColumns,
        HashSet<string>? metricColumns,
        bool isBenchmarkDotNetCsv)
    {
        if (excludedColumns.Contains(key))
            return true;

        if (metricColumns is null || !metricColumns.Contains(key))
            return false;

        return !isBenchmarkDotNetCsv || ParseNumericMetric(value, key).HasValue;
    }

    private static bool LooksLikeBenchmarkDotNetCsv(string[] headers)
    {
        var names = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        return !LooksLikeNormalizedRunnerCsv(names)
               && (names.Contains("Method") || names.Contains("Benchmark"))
               && headers.Any(IsBenchmarkDotNetStatisticColumn);
    }

    private static bool LooksLikeNormalizedRunnerCsv(HashSet<string> names)
        => names.Contains("Status")
           && (names.Contains("DurationMs") || names.Contains("Iteration"))
           && (names.Contains("Suite")
               || names.Contains("RunId")
               || names.Contains("Operation")
               || names.Contains("Engine"));

    private static bool IsBenchmarkDotNetStatisticColumn(string key)
    {
        var normalized = RemoveBracketUnit(key).Replace(" ", string.Empty);
        return BenchmarkDotNetStatisticColumns.Contains(normalized);
    }

    private static bool IsBenchmarkDotNetMetricColumn(string key)
    {
        var normalized = RemoveBracketUnit(key).Replace(" ", string.Empty);
        return BenchmarkDotNetStatisticColumns.Contains(normalized);
    }

    private static string? BenchmarkDotNetStatisticMetricName(string key)
    {
        var normalized = RemoveBracketUnit(key).Replace(" ", string.Empty);
        if (!BenchmarkDotNetStatisticColumns.Contains(normalized))
            return null;
        if (string.Equals(normalized, "Median", StringComparison.OrdinalIgnoreCase)) return "MedianMs";
        if (string.Equals(normalized, "Mean", StringComparison.OrdinalIgnoreCase)) return "MeanMs";
        if (string.Equals(normalized, "Min", StringComparison.OrdinalIgnoreCase)) return "MinMs";
        if (string.Equals(normalized, "Max", StringComparison.OrdinalIgnoreCase)) return "MaxMs";
        if (string.Equals(normalized, "StandardError", StringComparison.OrdinalIgnoreCase)) return "StdErr";
        if (string.Equals(normalized, "StandardDeviation", StringComparison.OrdinalIgnoreCase)) return "StdDev";
        return normalized;
    }

    private static string RemoveBracketUnit(string key)
    {
        var index = key.IndexOf('[');
        return index < 0 ? key.Trim() : key.Substring(0, index).Trim();
    }

    private static readonly HashSet<string> BenchmarkDotNetStatisticColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mean", "Median", "Min", "Max", "Q1", "Q3",
        "P0", "P25", "P50", "P75", "P90", "P95", "P99", "P100",
        "Error", "StdErr", "StdDev", "StandardError", "StandardDeviation", "Ratio", "RatioSD",
        "Gen0", "Gen1", "Gen2", "Allocated", "CodeSize", "OperationsPerSecond"
    };

    private static readonly HashSet<string> BenchmarkDotNetPrimaryDurationColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mean", "Median", "Min", "Max"
    };

    private static double BenchmarkDotNetJsonMetricFactor(string name)
    {
        var normalized = RemoveBracketUnit(name).Replace(" ", string.Empty);
        return BenchmarkDotNetPrimaryDurationColumns.Contains(normalized)
            || normalized is "Error" or "StdErr" or "StdDev" or "StandardError" or "StandardDeviation" or "Q1" or "Q3"
            || normalized.StartsWith("P", StringComparison.OrdinalIgnoreCase)
            ? 0.000001
            : 1.0;
    }

    private static readonly (string Suffix, double Factor)[] ByteUnits =
    {
        ("GiB", 1024d * 1024d * 1024d),
        ("MiB", 1024d * 1024d),
        ("KiB", 1024d),
        ("GB", 1024d * 1024d * 1024d),
        ("MB", 1024d * 1024d),
        ("KB", 1024d),
        ("B", 1d)
    };
}
