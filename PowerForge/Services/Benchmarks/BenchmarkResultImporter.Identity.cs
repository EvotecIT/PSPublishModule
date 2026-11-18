using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Options for importing benchmark artifacts.</summary>
public sealed class BenchmarkImportOptions
{
    /// <summary>Optional suite name override.</summary>
    public string? Suite { get; set; }

    /// <summary>Optional numeric culture for CSV artifacts.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Declared map from benchmark method (case) name to library id. When supplied, every imported row must
    /// resolve to a library through this map or a <c>library:&lt;id&gt;</c> BenchmarkDotNet category.
    /// </summary>
    public IDictionary<string, string>? LibraryMap { get; set; }

    /// <summary>Output validation recorded by the benchmark before timing.</summary>
    public BenchmarkOutputValidation[]? Validations { get; set; }

    /// <summary>Libraries whose licence acceptance the person running the benchmark gave explicitly.</summary>
    public string[]? AcceptedLibraries { get; set; }
}

public sealed partial class BenchmarkResultImporter
{
    /// <summary>BenchmarkDotNet category prefix that names a library, as in <c>[BenchmarkCategory("library:OfficeIMO")]</c>.</summary>
    public const string LibraryCategoryPrefix = "library:";

    private static readonly Regex RunnerVersion = new(@"v?(?<version>\d+(\.\d+)+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Imports a file or directory of benchmark artifacts with explicit library identity, output validation
    /// and licence acceptance. Everything is applied before production provenance is bound.
    /// </summary>
    /// <param name="path">Input file or directory path.</param>
    /// <param name="options">Import options.</param>
    /// <returns>Imported run result.</returns>
    public BenchmarkRunResult Import(string path, BenchmarkImportOptions options)
        => ImportCore(path, options ?? new BenchmarkImportOptions());

    private static void ApplyImportOptions(BenchmarkRunResult result, BenchmarkImportOptions options)
    {
        ApplyLibraryIdentity(result, options.LibraryMap);
        ApplyBenchmarkDotNetRunner(result);
        if (options.Validations is { Length: > 0 })
            result.Validations = result.Validations.Concat(options.Validations).ToArray();
        if (options.AcceptedLibraries is { Length: > 0 })
        {
            result.AcceptedLibraries = result.AcceptedLibraries
                .Concat(options.AcceptedLibraries.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static void ApplyLibraryIdentity(BenchmarkRunResult result, IDictionary<string, string>? map)
    {
        var normalizedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> item in map ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
                throw new ArgumentException("Library map entries need a method name and a library id.", nameof(map));
            normalizedMap[item.Key.Trim()] = item.Value.Trim();
        }

        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        foreach (BenchmarkSample sample in result.Samples)
        {
            sample.Library = ResolveLibrary(sample.Library, sample.Scenario, sample.Variables, normalizedMap, unresolved);
        }
        foreach (BenchmarkSummaryRow row in result.Summary)
        {
            row.Library = ResolveLibrary(row.Library, row.Scenario, row.Variables, normalizedMap, unresolved);
        }

        if (normalizedMap.Count > 0 && unresolved.Count > 0)
        {
            throw new InvalidOperationException(
                "The library map does not cover benchmark method(s) " + string.Join(", ", unresolved.Select(value => $"'{value}'")) +
                ". Map every method to a library or tag it with [BenchmarkCategory(\"library:<id>\")].");
        }
    }

    private static string ResolveLibrary(
        string? current,
        string scenario,
        IReadOnlyDictionary<string, string?> variables,
        IReadOnlyDictionary<string, string> map,
        ISet<string> unresolved)
    {
        if (!string.IsNullOrWhiteSpace(current))
            return current!.Trim();
        string? fromCategory = LibraryFromCategories(variables.TryGetValue("Categories", out string? categories) ? categories : null);
        if (fromCategory is not null)
            return fromCategory;
        if (map.TryGetValue(scenario ?? string.Empty, out string? mapped))
            return mapped;
        unresolved.Add(scenario ?? string.Empty);
        return string.Empty;
    }

    internal static string? LibraryFromCategories(string? categories)
    {
        if (string.IsNullOrWhiteSpace(categories))
            return null;
        string[] libraries = categories!
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.StartsWith(LibraryCategoryPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Substring(LibraryCategoryPrefix.Length).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (libraries.Length > 1)
            throw new InvalidOperationException($"Benchmark categories '{categories}' name more than one library.");
        return libraries.Length == 1 ? libraries[0] : null;
    }

    internal static string? ReadBenchmarkDotNetCategories(JsonElement benchmark)
    {
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(benchmark, "Categories", out JsonElement node))
            return null;
        if (node.ValueKind == JsonValueKind.String)
            return node.GetString();
        if (node.ValueKind != JsonValueKind.Array)
            return null;
        return string.Join(",", node.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()));
    }

    /// <summary>Counts warmup, measured iterations and launches recorded in BenchmarkDotNet JSON measurements.</summary>
    internal static (int? Warmups, int? Iterations, int? Launches) CountBenchmarkDotNetMeasurements(JsonElement benchmark)
    {
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(benchmark, "Measurements", out JsonElement measurements) ||
            measurements.ValueKind != JsonValueKind.Array)
            return (null, null, null);

        var launches = new HashSet<int>();
        int warmups = 0;
        int iterations = 0;
        foreach (JsonElement measurement in measurements.EnumerateArray())
        {
            if (measurement.ValueKind != JsonValueKind.Object)
                continue;
            int launch = GetInt32(measurement, "LaunchIndex") ?? 1;
            launches.Add(launch);
            if (launch != 1 || !string.Equals(GetString(measurement, "IterationMode"), "Workload", StringComparison.OrdinalIgnoreCase))
                continue;
            string? stage = GetString(measurement, "IterationStage");
            if (string.Equals(stage, "Warmup", StringComparison.OrdinalIgnoreCase))
                warmups++;
            else if (string.Equals(stage, "Actual", StringComparison.OrdinalIgnoreCase))
                iterations++;
        }
        return launches.Count == 0 ? (null, null, null) : (warmups, iterations, launches.Count);
    }

    private static void ApplyBenchmarkDotNetRunner(BenchmarkRunResult result)
    {
        if (result.Environment.Runner.IndexOf("BenchmarkDotNet", StringComparison.OrdinalIgnoreCase) < 0 &&
            !result.Metadata.Keys.Any(key => key.StartsWith(BenchmarkDotNetColumns.JobMetadataPrefix, StringComparison.OrdinalIgnoreCase)))
            return;

        BenchmarkRunnerInfo runner = result.Runner ??= new BenchmarkRunnerInfo();
        if (string.IsNullOrWhiteSpace(runner.Kind))
            runner.Kind = "benchmarkdotnet";
        if (string.IsNullOrWhiteSpace(runner.Version))
        {
            Match match = RunnerVersion.Match(result.Environment.Runner ?? string.Empty);
            runner.Version = match.Success ? match.Groups["version"].Value : string.Empty;
        }
        if (string.IsNullOrWhiteSpace(runner.Statistic))
            runner.Statistic = "median";
        if (string.IsNullOrWhiteSpace(runner.Ordering))
            runner.Ordering = "sequential";
        runner.Warmups ??= JobInt(result, "WarmupCount");
        runner.Iterations ??= JobInt(result, "IterationCount");
        runner.Launches ??= JobInt(result, "LaunchCount");
        if (runner.InProcess is null && JobValue(result, "Toolchain") is string toolchain)
            runner.InProcess = toolchain.IndexOf("InProcess", StringComparison.OrdinalIgnoreCase) >= 0;
        if (string.IsNullOrWhiteSpace(runner.Affinity) && JobValue(result, "Affinity") is string affinity)
            runner.Affinity = affinity;
    }

    private static string? JobValue(BenchmarkRunResult result, string name)
        => result.Metadata.TryGetValue(BenchmarkDotNetColumns.JobMetadataPrefix + name, out string? value) &&
           !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase)
            ? value.Trim()
            : null;

    private static int? JobInt(BenchmarkRunResult result, string name)
        => int.TryParse(JobValue(result, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
}
