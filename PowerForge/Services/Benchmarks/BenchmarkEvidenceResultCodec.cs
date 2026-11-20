using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Reads and writes the result files an evidence catalog publishes beside it.
/// </summary>
/// <remarks>
/// A published result stores each case once: its identity, variables, job and BenchmarkDotNet metrics, followed by
/// the measured samples as compact arrays. Durations and metrics keep six significant digits, alias metrics that
/// repeat another metric's value are written once, and the summary and comparison rows are rebuilt from the samples
/// when the file is read, so they always agree with the samples the catalog evaluated. Files written before this
/// format (a serialized <see cref="BenchmarkRunResult"/>) are still read.
/// </remarks>
internal static class BenchmarkEvidenceResultCodec
{
    internal const string FormatName = "powerforge.benchmark-result";
    internal const int FormatVersion = 2;
    private const int SignificantDigits = 6;

    // Alias metrics BenchmarkDotNet and the importers record next to a canonical metric with the same value.
    private static readonly (string Canonical, string[] Aliases)[] MetricAliases =
    {
        ("MinMs", new[] { "Min", "P0" }),
        ("MedianMs", new[] { "Median", "P50" }),
        ("MeanMs", new[] { "Mean" }),
        ("MaxMs", new[] { "Max", "P100" }),
        ("StdErrMs", new[] { "StandardError", "StdErr" }),
        ("StdDevMs", new[] { "StandardDeviation", "StdDev" }),
        ("P95Ms", new[] { "P95" }),
        ("P99Ms", new[] { "P99" }),
        ("Q1", new[] { "P25" }),
        ("Q3", new[] { "P75" }),
        ("Gen0", new[] { "Gen0Collections" }),
        ("Gen1", new[] { "Gen1Collections" }),
        ("Gen2", new[] { "Gen2Collections" }),
        ("BytesAllocatedPerOperation", new[] { "Allocated" })
    };

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Encodes a result in the published form.</summary>
    internal static byte[] Encode(BenchmarkRunResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        string json = JsonSerializer.Serialize(ToDocument(result), Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    /// <summary>Returns the result as it reads back from its published form.</summary>
    internal static BenchmarkRunResult Normalize(BenchmarkRunResult result)
        => Decode(Encode(result), "benchmark result");

    /// <summary>SHA-256 of the published form of a result.</summary>
    internal static string ComputeSha256(BenchmarkRunResult result) => ComputeSha256(Encode(result));

    internal static string ComputeSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>Whether the bytes hold a result in the published form rather than an earlier full result.</summary>
    internal static bool IsCompact(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutByteOrderMark(bytes));
            return IsCompact(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Files saved by editors may start with a UTF-8 byte order mark, which the JSON reader rejects.
    private static byte[] WithoutByteOrderMark(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.Skip(3).ToArray()
            : bytes;

    /// <summary>Whether the text holds a result in the published form.</summary>
    internal static bool IsCompact(string json)
        => IsCompact(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json));

    /// <summary>Whether a parsed document root is a result in the published form.</summary>
    internal static bool IsCompact(JsonElement root)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty("format", out JsonElement format) &&
           format.ValueKind == JsonValueKind.String &&
           string.Equals(format.GetString(), FormatName, StringComparison.Ordinal);

    /// <summary>The metric name a published result stores for an alias such as <c>Allocated</c> or <c>Gen0Collections</c>.</summary>
    internal static string CanonicalMetric(string? metric)
    {
        if (string.IsNullOrEmpty(metric))
            return metric ?? string.Empty;
        foreach ((string canonical, string[] aliases) in MetricAliases)
        {
            if (aliases.Any(alias => string.Equals(alias, metric, StringComparison.OrdinalIgnoreCase)))
                return canonical;
        }
        return metric!;
    }

    /// <summary>Reads a published result file in either form.</summary>
    internal static BenchmarkRunResult Read(string path)
        => Decode(File.ReadAllBytes(path), path);

    /// <summary>Decodes a published result in either form.</summary>
    internal static BenchmarkRunResult Decode(byte[] bytes, string sourceDescription)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        bytes = WithoutByteOrderMark(bytes);
        if (!IsCompact(bytes))
            return BenchmarkJson.ReadBytes<BenchmarkRunResult>(bytes, sourceDescription);

        CompactResult document = JsonSerializer.Deserialize<CompactResult>(bytes, Options)
                                 ?? throw new InvalidOperationException($"Unable to deserialize benchmark JSON: {sourceDescription}");
        if (document.FormatVersion != FormatVersion)
        {
            throw new InvalidOperationException(
                $"Benchmark result '{sourceDescription}' uses format version {document.FormatVersion}; only {FormatVersion} is supported.");
        }
        return FromDocument(document, sourceDescription);
    }

    private static CompactResult ToDocument(BenchmarkRunResult result)
    {
        BenchmarkSample[] samples = result.Samples ?? Array.Empty<BenchmarkSample>();
        string defaultRunId = MostCommon(samples.Select(sample => sample.RunId)) ?? result.RunId ?? string.Empty;
        string defaultSuite = MostCommon(samples.Select(sample => sample.Suite)) ?? result.Suite ?? string.Empty;
        string defaultHost = MostCommon(samples.Select(sample => sample.Host)) ?? string.Empty;
        string defaultOs = MostCommon(samples.Select(sample => sample.Os)) ?? string.Empty;
        string defaultRunMode = MostCommon(samples.Select(sample => sample.RunMode)) ?? string.Empty;

        var engines = new List<string>();
        var cases = new List<CompactCase>();
        // Cases keep the order in which their first sample was recorded.
        foreach (IGrouping<string, BenchmarkSample> group in samples.GroupBy(SampleCaseKey, StringComparer.Ordinal))
        {
            BenchmarkSample first = group.First();
            BenchmarkSample[] caseSamples = group.ToArray();
            int engine = engines.IndexOf(first.Engine ?? string.Empty);
            if (engine < 0)
            {
                engines.Add(first.Engine ?? string.Empty);
                engine = engines.Count - 1;
            }

            var compact = new CompactCase
            {
                RunId = Differs(first.RunId, defaultRunId),
                Suite = Differs(first.Suite, defaultSuite),
                Scenario = NullIfEmpty(first.Scenario),
                Operation = NullIfEmpty(first.Operation),
                Engine = engine,
                Library = NullIfEmpty(first.Library),
                Host = Differs(first.Host, defaultHost),
                Os = Differs(first.Os, defaultOs),
                RunMode = Differs(first.RunMode, defaultRunMode),
                Variables = SortedVariables(first.Variables),
                DurationsMs = caseSamples.Select(sample => Round(sample.DurationMs)).ToArray()
            };

            int[] iterations = caseSamples.Select(sample => sample.Iteration).ToArray();
            if (!iterations.SequenceEqual(Enumerable.Range(0, iterations.Length)))
                compact.Iterations = iterations;
            if (caseSamples.Any(sample => sample.Status != BenchmarkSampleStatus.Succeeded))
                compact.Statuses = caseSamples.Select(sample => sample.Status.ToString()).ToArray();
            if (caseSamples.Any(sample => !string.IsNullOrEmpty(sample.Reason)))
                compact.Reasons = caseSamples.Select(sample => sample.Reason ?? string.Empty).ToArray();
            if (caseSamples.Any(sample => sample.AllocatedBytes.HasValue))
                compact.AllocatedBytes = caseSamples.Select(sample => sample.AllocatedBytes).ToArray();
            if (caseSamples.Any(sample => sample.WorkingSetDeltaBytes.HasValue))
                compact.WorkingSetDeltaBytes = caseSamples.Select(sample => sample.WorkingSetDeltaBytes).ToArray();
            if (caseSamples.Any(sample => sample.OutputMetric.HasValue))
                compact.OutputMetrics = caseSamples.Select(sample => RoundNullable(sample.OutputMetric)).ToArray();

            // BenchmarkDotNet repeats its case statistics on every sample; they are stored once. Samples whose
            // metrics differ keep their own.
            SortedDictionary<string, double>[] metrics = caseSamples.Select(sample => CompactMetrics(sample.Metrics)).ToArray();
            if (metrics.All(value => MetricsEqual(value, metrics[0])))
                compact.Metrics = metrics[0].Count == 0 ? null : metrics[0];
            else
                compact.SampleMetrics = metrics;
            cases.Add(compact);
        }

        // Summary rows without samples (a case that failed before it was measured) cannot be rebuilt and are kept.
        var sampledRows = new HashSet<string>(samples.Select(SampleRowKey), StringComparer.Ordinal);
        CompactSummary[] unsampled = (result.Summary ?? Array.Empty<BenchmarkSummaryRow>())
            .Where(row => !sampledRows.Contains(SummaryCaseKey(row)))
            .Select(row => ToCompactSummary(row, engines))
            .ToArray();

        var comparisons = (result.Comparison ?? Array.Empty<BenchmarkComparisonRow>())
            .Select(row => new CompactComparison
            {
                BaselineEngine = row.BaselineEngine ?? string.Empty,
                Metric = string.IsNullOrEmpty(row.Metric) ? "MedianMs" : row.Metric,
                TieTolerance = row.TieTolerance
            })
            .GroupBy(value => value.BaselineEngine + "\u001f" + value.Metric + "\u001f" + value.TieTolerance.ToString("R", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return new CompactResult
        {
            Format = FormatName,
            FormatVersion = FormatVersion,
            RunId = NullIfEmpty(result.RunId),
            Suite = NullIfEmpty(result.Suite),
            StartedUtc = result.StartedUtc,
            FinishedUtc = result.FinishedUtc,
            Environment = result.Environment,
            Runner = result.Runner,
            Metadata = SortedOrNull(result.Metadata),
            Artifacts = SortedOrNull(result.Artifacts),
            // Every benchmark process records the validation it ran, so identical records repeat once per case.
            Validations = result.Validations is { Length: > 0 } ? DistinctValidations(result.Validations) : null,
            AcceptedLibraries = result.AcceptedLibraries is { Length: > 0 } ? result.AcceptedLibraries : null,
            Defaults = new CompactDefaults
            {
                RunId = NullIfEmpty(defaultRunId),
                Suite = NullIfEmpty(defaultSuite),
                Host = NullIfEmpty(defaultHost),
                Os = NullIfEmpty(defaultOs),
                RunMode = NullIfEmpty(defaultRunMode)
            },
            Engines = engines.ToArray(),
            Cases = cases.ToArray(),
            UnsampledSummary = unsampled.Length == 0 ? null : unsampled,
            Comparisons = comparisons.Length == 0 ? null : comparisons
        };
    }

    private static BenchmarkRunResult FromDocument(CompactResult document, string sourceDescription)
    {
        CompactDefaults defaults = document.Defaults ?? new CompactDefaults();
        string[] engines = document.Engines ?? Array.Empty<string>();
        string[] aliases = (document.Comparisons ?? Array.Empty<CompactComparison>())
            .Select(definition => definition.Metric)
            .Where(metric => !string.Equals(CanonicalMetric(metric), metric, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var samples = new List<BenchmarkSample>();
        foreach (CompactCase item in document.Cases ?? Array.Empty<CompactCase>())
        {
            double[] durations = item.DurationsMs ?? Array.Empty<double>();
            int count = durations.Length;
            RequireLength(item.Iterations, count, "iterations", sourceDescription);
            RequireLength(item.Statuses, count, "statuses", sourceDescription);
            RequireLength(item.Reasons, count, "reasons", sourceDescription);
            RequireLength(item.AllocatedBytes, count, "allocatedBytes", sourceDescription);
            RequireLength(item.WorkingSetDeltaBytes, count, "workingSetDeltaBytes", sourceDescription);
            RequireLength(item.OutputMetrics, count, "outputMetrics", sourceDescription);
            RequireLength(item.SampleMetrics, count, "sampleMetrics", sourceDescription);
            string engine = Engine(engines, item.Engine, sourceDescription);
            for (int index = 0; index < count; index++)
            {
                BenchmarkSampleStatus status = BenchmarkSampleStatus.Succeeded;
                if (item.Statuses is not null &&
                    !Enum.TryParse(item.Statuses[index], ignoreCase: false, out status))
                {
                    throw new InvalidOperationException(
                        $"Benchmark result '{sourceDescription}' has an unknown sample status '{item.Statuses[index]}'.");
                }
                samples.Add(new BenchmarkSample
                {
                    RunId = item.RunId ?? defaults.RunId ?? string.Empty,
                    Suite = item.Suite ?? defaults.Suite ?? string.Empty,
                    Scenario = item.Scenario ?? string.Empty,
                    Operation = item.Operation ?? string.Empty,
                    Engine = engine,
                    Library = item.Library ?? string.Empty,
                    Host = item.Host ?? defaults.Host ?? string.Empty,
                    Os = item.Os ?? defaults.Os ?? string.Empty,
                    RunMode = item.RunMode ?? defaults.RunMode ?? string.Empty,
                    Iteration = item.Iterations?[index] ?? index,
                    Status = status,
                    DurationMs = durations[index],
                    AllocatedBytes = item.AllocatedBytes?[index],
                    WorkingSetDeltaBytes = item.WorkingSetDeltaBytes?[index],
                    OutputMetric = item.OutputMetrics?[index],
                    Reason = item.Reasons?[index] ?? string.Empty,
                    Variables = new Dictionary<string, string?>(item.Variables ?? new SortedDictionary<string, string?>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase),
                    Metrics = new Dictionary<string, double>(
                        WithAliases(item.SampleMetrics?[index] ?? item.Metrics ?? new SortedDictionary<string, double>(StringComparer.Ordinal), aliases),
                        StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        var result = new BenchmarkRunResult
        {
            RunId = document.RunId ?? string.Empty,
            Suite = document.Suite ?? string.Empty,
            StartedUtc = document.StartedUtc,
            FinishedUtc = document.FinishedUtc,
            Samples = samples.ToArray(),
            Environment = document.Environment ?? new BenchmarkEnvironmentInfo(),
            Runner = document.Runner,
            Metadata = new Dictionary<string, string>(document.Metadata ?? new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase),
            Artifacts = new Dictionary<string, string>(document.Artifacts ?? new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase),
            Validations = document.Validations ?? Array.Empty<BenchmarkOutputValidation>(),
            AcceptedLibraries = document.AcceptedLibraries ?? Array.Empty<string>()
        };

        // Summary rows are rebuilt with the outlier policy the run recorded, so they always match the samples.
        var summary = new BenchmarkSummaryService();
        BenchmarkSummaryRow[] rows = summary.Summarize(result.Samples, OutlierMode(result.Metadata));
        BenchmarkSummaryRow[] unsampled = (document.UnsampledSummary ?? Array.Empty<CompactSummary>())
            .Select(row => FromCompactSummary(row, engines, defaults, sourceDescription))
            .ToArray();
        result.Summary = rows.Concat(unsampled).ToArray();
        if (document.Comparisons is { Length: > 0 })
        {
            result.Comparison = document.Comparisons
                .SelectMany(definition => summary.Compare(result.Summary, definition.BaselineEngine, definition.Metric, definition.TieTolerance))
                .ToArray();
        }
        return result;
    }

    // A comparison may name a metric by an alias the published form stores once under its canonical name. The alias
    // is restored on the samples, so the rebuilt summary rows carry it and the comparison resolves its values.
    private static SortedDictionary<string, double> WithAliases(SortedDictionary<string, double> metrics, string[] aliases)
    {
        if (aliases.Length == 0)
            return metrics;
        var restored = new SortedDictionary<string, double>(metrics, StringComparer.Ordinal);
        foreach (string alias in aliases)
        {
            string canonical = CanonicalMetric(alias);
            string? key = restored.Keys.FirstOrDefault(name => string.Equals(name, canonical, StringComparison.OrdinalIgnoreCase));
            if (key is not null && !restored.Keys.Any(name => string.Equals(name, alias, StringComparison.OrdinalIgnoreCase)))
                restored[alias] = restored[key];
        }
        return restored;
    }

    private static CompactSummary ToCompactSummary(BenchmarkSummaryRow row, List<string> engines)
    {
        int engine = engines.IndexOf(row.Engine ?? string.Empty);
        if (engine < 0)
        {
            engines.Add(row.Engine ?? string.Empty);
            engine = engines.Count - 1;
        }
        SortedDictionary<string, double> metrics = CompactMetrics(row.Metrics);
        return new CompactSummary
        {
            Suite = NullIfEmpty(row.Suite),
            Scenario = NullIfEmpty(row.Scenario),
            Operation = NullIfEmpty(row.Operation),
            Engine = engine,
            Library = NullIfEmpty(row.Library),
            Host = NullIfEmpty(row.Host),
            Os = NullIfEmpty(row.Os),
            RunMode = NullIfEmpty(row.RunMode),
            Variables = SortedVariables(row.Variables),
            SampleCount = row.SampleCount,
            FailureCount = row.FailureCount,
            OutlierCount = row.OutlierCount,
            Status = NullIfEmpty(row.Status),
            MedianMs = RoundNullable(row.MedianMs),
            MeanMs = RoundNullable(row.MeanMs),
            MinMs = RoundNullable(row.MinMs),
            MaxMs = RoundNullable(row.MaxMs),
            P95Ms = RoundNullable(row.P95Ms),
            P99Ms = RoundNullable(row.P99Ms),
            StdDevMs = RoundNullable(row.StdDevMs),
            StdErrMs = RoundNullable(row.StdErrMs),
            ConfidenceInterval = row.ConfidenceInterval is null ? null : new BenchmarkConfidenceInterval
            {
                Level = row.ConfidenceInterval.Level,
                LowerMs = Round(row.ConfidenceInterval.LowerMs),
                UpperMs = Round(row.ConfidenceInterval.UpperMs)
            },
            FailureReasons = row.FailureReasons is { Count: > 0 }
                ? new SortedDictionary<string, int>(row.FailureReasons, StringComparer.Ordinal)
                : null,
            Metrics = metrics.Count == 0 ? null : metrics
        };
    }

    private static BenchmarkSummaryRow FromCompactSummary(
        CompactSummary row,
        string[] engines,
        CompactDefaults defaults,
        string sourceDescription)
        => new()
        {
            Suite = row.Suite ?? defaults.Suite ?? string.Empty,
            Scenario = row.Scenario ?? string.Empty,
            Operation = row.Operation ?? string.Empty,
            Engine = Engine(engines, row.Engine, sourceDescription),
            Library = row.Library ?? string.Empty,
            Host = row.Host ?? string.Empty,
            Os = row.Os ?? string.Empty,
            RunMode = row.RunMode ?? string.Empty,
            Variables = new Dictionary<string, string?>(row.Variables ?? new SortedDictionary<string, string?>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase),
            SampleCount = row.SampleCount,
            FailureCount = row.FailureCount,
            OutlierCount = row.OutlierCount,
            Status = row.Status ?? string.Empty,
            MedianMs = row.MedianMs,
            MeanMs = row.MeanMs,
            MinMs = row.MinMs,
            MaxMs = row.MaxMs,
            P95Ms = row.P95Ms,
            P99Ms = row.P99Ms,
            StdDevMs = row.StdDevMs,
            StdErrMs = row.StdErrMs,
            ConfidenceInterval = row.ConfidenceInterval,
            FailureReasons = new Dictionary<string, int>(row.FailureReasons ?? new SortedDictionary<string, int>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(row.Metrics ?? new SortedDictionary<string, double>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase)
        };

    private static BenchmarkOutputValidation[] DistinctValidations(IEnumerable<BenchmarkOutputValidation> validations)
        => validations
            .GroupBy(value => (value.Library ?? string.Empty) + "" + (value.Case ?? string.Empty) + "" +
                              (value.Status?.ToString() ?? string.Empty) + "" + (value.Detail ?? string.Empty), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    /// <summary>Rounds a measured value to the six significant digits published results keep.</summary>
    internal static double Round(double value)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
            return value;
        return double.Parse(
            value.ToString("G" + SignificantDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    private static double? RoundNullable(double? value) => value.HasValue ? Round(value.Value) : null;

    private static SortedDictionary<string, double> CompactMetrics(IReadOnlyDictionary<string, double>? metrics)
    {
        var compact = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (metrics is null)
            return compact;
        foreach (KeyValuePair<string, double> metric in metrics)
            compact[metric.Key] = metric.Value;
        foreach ((string canonical, string[] aliases) in MetricAliases)
        {
            string? canonicalKey = FindKey(compact, canonical);
            if (canonicalKey is null)
                continue;
            double canonicalValue = Round(compact[canonicalKey]);
            foreach (string alias in aliases)
            {
                // Values are compared as they are written, so encoding a decoded result removes the same aliases.
                string? aliasKey = FindKey(compact, alias);
                if (aliasKey is not null && Round(compact[aliasKey]).Equals(canonicalValue))
                    compact.Remove(aliasKey);
            }
        }
        foreach (string key in compact.Keys.ToArray())
            compact[key] = Round(compact[key]);
        return compact;
    }

    private static string? FindKey(SortedDictionary<string, double> values, string name)
        => values.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static bool MetricsEqual(SortedDictionary<string, double> left, SortedDictionary<string, double> right)
        => left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out double value) && value.Equals(item.Value));

    private static SortedDictionary<string, string?>? SortedVariables(IReadOnlyDictionary<string, string?>? variables)
        => variables is null || variables.Count == 0
            ? null
            : new SortedDictionary<string, string?>(variables.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static SortedDictionary<string, string>? SortedOrNull(IReadOnlyDictionary<string, string>? values)
        => values is null || values.Count == 0
            ? null
            : new SortedDictionary<string, string>(values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string SampleCaseKey(BenchmarkSample sample)
        => CaseKey(sample.RunId, sample.Suite, sample.Scenario, sample.Operation, sample.Engine, sample.Library, sample.Host, sample.Os, sample.RunMode, sample.Variables);

    // Summary rows aggregate every run's samples of a case, so they are matched without the run id.
    private static string SampleRowKey(BenchmarkSample sample)
        => CaseKey(null, sample.Suite, sample.Scenario, sample.Operation, sample.Engine, sample.Library, sample.Host, sample.Os, sample.RunMode, sample.Variables);

    private static string SummaryCaseKey(BenchmarkSummaryRow row)
        => CaseKey(null, row.Suite, row.Scenario, row.Operation, row.Engine, row.Library, row.Host, row.Os, row.RunMode, row.Variables);

    private static string CaseKey(
        string? runId,
        string? suite,
        string? scenario,
        string? operation,
        string? engine,
        string? library,
        string? host,
        string? os,
        string? runMode,
        IReadOnlyDictionary<string, string?>? variables)
    {
        var builder = new StringBuilder();
        foreach (string? part in new[] { suite, scenario, operation, engine, library, host, os, runMode })
            builder.Append(part ?? string.Empty).Append('\u001f');
        foreach (KeyValuePair<string, string?> variable in (variables ?? new Dictionary<string, string?>())
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(variable.Key.ToUpperInvariant())
                .Append(variable.Value is null ? "\u0000" : "=" + variable.Value)
                .Append('\u001e');
        }
        if (runId is not null)
            builder.Append('\u001d').Append(runId);
        return builder.ToString();
    }

    private static string? MostCommon(IEnumerable<string?> values)
        => values
            .Select(value => value ?? string.Empty)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

    private static string? Differs(string? value, string defaultValue)
        => string.Equals(value ?? string.Empty, defaultValue, StringComparison.Ordinal) ? null : value ?? string.Empty;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Engine(string[] engines, int index, string sourceDescription)
    {
        if (index < 0 || index >= engines.Length)
            throw new InvalidOperationException($"Benchmark result '{sourceDescription}' refers to an unknown engine {index}.");
        return engines[index];
    }

    private static void RequireLength<T>(T[]? values, int count, string name, string sourceDescription)
    {
        if (values is not null && values.Length != count)
        {
            throw new InvalidOperationException(
                $"Benchmark result '{sourceDescription}' has {values.Length} {name} for {count} samples.");
        }
    }

    private static PowerShellBenchmarkOutlierMode OutlierMode(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (string key in new[] { "outlierMode", "benchmark.outlierMode", "benchmark.execution.outlierMode" })
        {
            foreach (KeyValuePair<string, string> item in metadata)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(item.Value, ignoreCase: true, out PowerShellBenchmarkOutlierMode mode) &&
                    Enum.IsDefined(typeof(PowerShellBenchmarkOutlierMode), mode))
                {
                    return mode;
                }
            }
        }
        return PowerShellBenchmarkOutlierMode.None;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>());
        options.Converters.Add(new BenchmarkKebabCaseEnumConverter<BenchmarkLibraryRestriction>());
        options.Converters.Add(new BenchmarkKebabCaseEnumConverter<BenchmarkValidationStatus>());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class CompactResult
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public string? RunId { get; set; }
        public string? Suite { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset FinishedUtc { get; set; }
        public BenchmarkEnvironmentInfo? Environment { get; set; }
        public BenchmarkRunnerInfo? Runner { get; set; }
        public SortedDictionary<string, string>? Metadata { get; set; }
        public SortedDictionary<string, string>? Artifacts { get; set; }
        public BenchmarkOutputValidation[]? Validations { get; set; }
        public string[]? AcceptedLibraries { get; set; }
        public CompactDefaults? Defaults { get; set; }
        public string[]? Engines { get; set; }
        public CompactCase[]? Cases { get; set; }
        public CompactSummary[]? UnsampledSummary { get; set; }
        public CompactComparison[]? Comparisons { get; set; }
    }

    private sealed class CompactDefaults
    {
        public string? RunId { get; set; }
        public string? Suite { get; set; }
        public string? Host { get; set; }
        public string? Os { get; set; }
        public string? RunMode { get; set; }
    }

    private sealed class CompactCase
    {
        public string? Library { get; set; }
        public string? Scenario { get; set; }
        public string? Operation { get; set; }
        public int Engine { get; set; }
        public string? RunId { get; set; }
        public string? Suite { get; set; }
        public string? Host { get; set; }
        public string? Os { get; set; }
        public string? RunMode { get; set; }
        public SortedDictionary<string, string?>? Variables { get; set; }
        public SortedDictionary<string, double>? Metrics { get; set; }
        public double[]? DurationsMs { get; set; }
        public long?[]? AllocatedBytes { get; set; }
        public long?[]? WorkingSetDeltaBytes { get; set; }
        public double?[]? OutputMetrics { get; set; }
        public int[]? Iterations { get; set; }
        public string[]? Statuses { get; set; }
        public string[]? Reasons { get; set; }
        public SortedDictionary<string, double>[]? SampleMetrics { get; set; }
    }

    private sealed class CompactSummary
    {
        public string? Library { get; set; }
        public string? Scenario { get; set; }
        public string? Operation { get; set; }
        public int Engine { get; set; }
        public string? Suite { get; set; }
        public string? Host { get; set; }
        public string? Os { get; set; }
        public string? RunMode { get; set; }
        public SortedDictionary<string, string?>? Variables { get; set; }
        public int SampleCount { get; set; }
        public int FailureCount { get; set; }
        public int OutlierCount { get; set; }
        public string? Status { get; set; }
        public double? MedianMs { get; set; }
        public double? MeanMs { get; set; }
        public double? MinMs { get; set; }
        public double? MaxMs { get; set; }
        public double? P95Ms { get; set; }
        public double? P99Ms { get; set; }
        public double? StdDevMs { get; set; }
        public double? StdErrMs { get; set; }
        public BenchmarkConfidenceInterval? ConfidenceInterval { get; set; }
        public SortedDictionary<string, int>? FailureReasons { get; set; }
        public SortedDictionary<string, double>? Metrics { get; set; }
    }

    private sealed class CompactComparison
    {
        public string BaselineEngine { get; set; } = string.Empty;
        public string Metric { get; set; } = "MedianMs";
        public double TieTolerance { get; set; }
    }
}
