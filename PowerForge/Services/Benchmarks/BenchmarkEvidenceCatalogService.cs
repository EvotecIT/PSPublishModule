namespace PowerForge;

/// <summary>
/// Builds and validates a platform-aware benchmark evidence catalog.
/// </summary>
public sealed class BenchmarkEvidenceCatalogService
{
    private static readonly string[] DefaultExpectedPlatforms = { "windows", "linux", "macos" };

    /// <summary>
    /// Adds or replaces one platform/run-mode lane and recomputes comparison and availability state.
    /// </summary>
    /// <param name="catalog">Existing catalog, or <see langword="null"/> for a new catalog.</param>
    /// <param name="result">Normalized benchmark result.</param>
    /// <param name="comparisonId">Stable identifier for one equivalent workload definition.</param>
    /// <param name="resultPath">Portable path or URL to the normalized run result.</param>
    /// <param name="runMode">Run mode such as quick or full.</param>
    /// <param name="publish">Whether the lane supports public benchmark claims.</param>
    /// <param name="expectedPlatforms">Platforms expected for complete evidence.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog Update(
        BenchmarkEvidenceCatalog? catalog,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(comparisonId)) throw new ArgumentException("Comparison identifier is required.", nameof(comparisonId));
        if (string.IsNullOrWhiteSpace(resultPath)) throw new ArgumentException("Result path is required.", nameof(resultPath));
        if (string.IsNullOrWhiteSpace(runMode)) throw new ArgumentException("Run mode is required.", nameof(runMode));
        if (publish)
            ValidatePublishableResult(result);

        catalog ??= new BenchmarkEvidenceCatalog();
        catalog.SchemaVersion = 2;
        catalog.ExpectedPlatforms = NormalizeExpectedPlatforms(expectedPlatforms ?? catalog.ExpectedPlatforms);

        var platform = BenchmarkPlatformNormalizer.NormalizeId(result.Environment.OsFamily);
        if (string.IsNullOrWhiteSpace(platform))
            platform = BenchmarkPlatformNormalizer.NormalizeId(MetadataValue(result.Metadata, "osLabel", "os"));
        if (string.IsNullOrWhiteSpace(platform))
            platform = BenchmarkPlatformNormalizer.NormalizeId(result.Summary.Select(row => row.Os).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(platform))
            throw new InvalidOperationException("The benchmark result does not identify its operating-system platform.");

        var entry = new BenchmarkEvidenceEntry
        {
            ComparisonId = comparisonId.Trim(),
            Platform = platform,
            RunMode = runMode.Trim().ToLowerInvariant(),
            GeneratedUtc = result.FinishedUtc == default ? DateTimeOffset.UtcNow : result.FinishedUtc,
            Publish = publish,
            ResultPath = resultPath,
            Suite = result.Suite,
            Environment = CopyEnvironment(result.Environment),
            Compatibility = BuildCompatibility(result)
        };

        var entries = catalog.Entries
            .Where(existing => !SameLane(existing, entry))
            .Append(entry)
            .OrderBy(existing => existing.ComparisonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(existing => existing.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenBy(existing => existing.RunMode, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(existing => existing.GeneratedUtc)
            .ToArray();

        ApplyCompatibility(entries);
        catalog.Entries = entries;
        catalog.Availability = BuildAvailability(entries, catalog.ExpectedPlatforms);
        return catalog;
    }

    /// <summary>
    /// Reads an existing catalog, updates it, and writes the new JSON atomically through the common serializer.
    /// </summary>
    /// <param name="catalogPath">Catalog JSON path.</param>
    /// <param name="result">Normalized benchmark result.</param>
    /// <param name="comparisonId">Stable identifier for one equivalent workload definition.</param>
    /// <param name="resultPath">Portable path or URL to the normalized run result.</param>
    /// <param name="runMode">Run mode such as quick or full.</param>
    /// <param name="publish">Whether the lane supports public benchmark claims.</param>
    /// <param name="expectedPlatforms">Platforms expected for complete evidence.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog UpdateFile(
        string catalogPath,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath)) throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        string fullPath = Path.GetFullPath(catalogPath);
        using var fileLease = BenchmarkFileUpdateLock.Acquire(fullPath);
        var catalog = File.Exists(fullPath)
            ? BenchmarkJson.Read<BenchmarkEvidenceCatalog>(fullPath)
            : null;
        var updated = Update(catalog, result, comparisonId, resultPath, runMode, publish, expectedPlatforms);
        BenchmarkJson.Write(fullPath, updated);
        return updated;
    }

    private static string[] NormalizeExpectedPlatforms(IEnumerable<string>? platforms)
    {
        var normalized = (platforms ?? DefaultExpectedPlatforms)
            .Select(BenchmarkPlatformNormalizer.NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? DefaultExpectedPlatforms.ToArray() : normalized;
    }

    private static Dictionary<string, string> BuildCompatibility(BenchmarkRunResult result)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["suite"] = result.Suite
        };
        foreach (var item in result.Metadata.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Key.StartsWith("benchmark.fixture.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.package.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.workload.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("gitSha", StringComparison.OrdinalIgnoreCase))
            {
                dimensions[item.Key] = item.Value;
            }
        }

        return dimensions;
    }

    private static void ApplyCompatibility(BenchmarkEvidenceEntry[] entries)
    {
        foreach (var group in entries.GroupBy(
                     entry => string.Join("\u001f", entry.ComparisonId, entry.RunMode),
                     StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.Where(entry => entry.Publish).ToArray();
            if (candidates.Length == 0)
                candidates = group.ToArray();
            var issues = CompareDimensions(candidates);
            foreach (var entry in group)
            {
                entry.Comparable = issues.Length == 0;
                entry.CompatibilityIssues = issues;
            }
        }
    }

    private static string[] CompareDimensions(IReadOnlyCollection<BenchmarkEvidenceEntry> entries)
    {
        var keys = entries
            .SelectMany(entry => entry.Compatibility.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        foreach (var key in keys)
        {
            var values = entries
                .Select(entry => TryGetValueIgnoreCase(entry.Compatibility, key, out var value) ? value : "<missing>")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (values.Length > 1)
                issues.Add($"{key}: lanes contain incompatible values ({string.Join(", ", values.Select(value => $"'{value}'"))}).");
        }

        return issues.ToArray();
    }

    private static void ValidatePublishableResult(BenchmarkRunResult result)
    {
        if (string.IsNullOrWhiteSpace(MetadataValue(result.Metadata, "gitSha")))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires exact source provenance in metadata key 'gitSha'.");
        }

        bool hasFailedMeasurement =
            result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Failed) ||
            result.Summary.Any(row => string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        bool hasSuccessfulMeasurement =
            result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Succeeded) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                (row.MedianMs.HasValue || row.MeanMs.HasValue));
        if (hasFailedMeasurement || !hasSuccessfulMeasurement)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires at least one successful measurement and no failed measurements.");
        }
    }

    private static bool TryGetValueIgnoreCase(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value)
    {
        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            value = item.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static BenchmarkPlatformAvailability[] BuildAvailability(
        IEnumerable<BenchmarkEvidenceEntry> entries,
        IEnumerable<string> expectedPlatforms)
    {
        var all = entries.ToArray();
        return all
            .GroupBy(
                entry => string.Join("\u001f", entry.ComparisonId, entry.RunMode),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                BenchmarkEvidenceEntry first = group.First();
                return (first.ComparisonId, first.RunMode);
            })
            .OrderBy(group => group.ComparisonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.RunMode, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => expectedPlatforms.Select(platform =>
            {
                bool requirePublishedLane = string.Equals(group.RunMode, "full", StringComparison.OrdinalIgnoreCase);
                var matches = all.Where(entry =>
                        string.Equals(entry.ComparisonId, group.ComparisonId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.RunMode, group.RunMode, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                        (!requirePublishedLane || entry.Publish))
                    .ToArray();
                return new BenchmarkPlatformAvailability
                {
                    ComparisonId = group.ComparisonId,
                    RunMode = group.RunMode,
                    Platform = platform,
                    Available = matches.Length > 0,
                    RunModes = matches.Length == 0 ? Array.Empty<string>() : new[] { group.RunMode },
                    LatestUtc = matches.Length == 0 ? null : matches.Max(entry => entry.GeneratedUtc)
                };
            }))
            .ToArray();
    }

    private static bool SameLane(BenchmarkEvidenceEntry left, BenchmarkEvidenceEntry right)
        => string.Equals(left.ComparisonId, right.ComparisonId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Platform, right.Platform, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.RunMode, right.RunMode, StringComparison.OrdinalIgnoreCase);

    private static string MetadataValue(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var item in metadata)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                    return item.Value;
            }
        }

        return string.Empty;
    }

    private static BenchmarkEnvironmentInfo CopyEnvironment(BenchmarkEnvironmentInfo value)
        => new()
        {
            OsFamily = value.OsFamily,
            OsDescription = value.OsDescription,
            OsArchitecture = value.OsArchitecture,
            ProcessArchitecture = value.ProcessArchitecture,
            ProcessorName = value.ProcessorName,
            PhysicalProcessorCount = value.PhysicalProcessorCount,
            PhysicalCoreCount = value.PhysicalCoreCount,
            LogicalCoreCount = value.LogicalCoreCount,
            RuntimeVersion = value.RuntimeVersion,
            DotNetSdkVersion = value.DotNetSdkVersion,
            Runner = value.Runner,
            MachineName = value.MachineName
        };
}
