using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Builds and validates a platform-aware benchmark evidence catalog.
/// </summary>
public sealed class BenchmarkEvidenceCatalogService
{
    private const int SupportedSchemaVersion = 2;
    private static readonly string[] DefaultExpectedPlatforms = { "windows", "linux", "macos" };
    private static readonly string[] ExecutionPolicyMetadataKeys =
    {
        "profile",
        "cleanup",
        "warmupCount",
        "iterationCount",
        "runOrder",
        "memoryCleanup",
        "cooldownMilliseconds",
        "outlierMode",
        "runMode"
    };

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
    /// <param name="platform">Producing platform override for artifacts that do not carry OS metadata.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog Update(
        BenchmarkEvidenceCatalog? catalog,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(comparisonId)) throw new ArgumentException("Comparison identifier is required.", nameof(comparisonId));
        if (string.IsNullOrWhiteSpace(resultPath)) throw new ArgumentException("Result path is required.", nameof(resultPath));
        if (string.IsNullOrWhiteSpace(runMode)) throw new ArgumentException("Run mode is required.", nameof(runMode));
        string normalizedRunMode = runMode.Trim().ToLowerInvariant();
        if (publish && !string.Equals(normalizedRunMode, "full", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Only Full benchmark runs can be published as evidence for public claims.");
        }
        if (publish)
        {
            ValidateEmbeddedRunModes(result, normalizedRunMode);
            ValidatePublishableResult(result);
        }

        ValidateCatalogSchema(catalog);
        catalog ??= new BenchmarkEvidenceCatalog();
        catalog.SchemaVersion = SupportedSchemaVersion;
        catalog.ExpectedPlatforms = NormalizeExpectedPlatforms(expectedPlatforms ?? catalog.ExpectedPlatforms);

        string resolvedPlatform = ResolvePlatform(result, platform);

        var entry = new BenchmarkEvidenceEntry
        {
            ComparisonId = comparisonId.Trim(),
            Platform = resolvedPlatform,
            RunMode = normalizedRunMode,
            GeneratedUtc = result.FinishedUtc == default ? DateTimeOffset.UtcNow : result.FinishedUtc,
            Publish = publish,
            ResultPath = resultPath,
            Suite = result.Suite,
            Environment = CopyEnvironment(result.Environment),
            Compatibility = BuildCompatibility(result)
        };

        BenchmarkEvidenceEntry? existingLane = catalog.Entries
            .Where(existing => SameLane(existing, entry))
            .OrderByDescending(existing => existing.GeneratedUtc)
            .FirstOrDefault();
        BenchmarkEvidenceEntry selectedEntry = existingLane is not null &&
                                               existingLane.GeneratedUtc > entry.GeneratedUtc
            ? existingLane
            : entry;
        var entries = catalog.Entries
            .Where(existing => !SameLane(existing, entry))
            .Append(selectedEntry)
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
    /// <param name="platform">Producing platform override for artifacts that do not carry OS metadata.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog UpdateFile(
        string catalogPath,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath)) throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        string fullPath = BenchmarkJson.ResolveWritePath(catalogPath);
        using var fileLease = BenchmarkFileUpdateLock.Acquire(fullPath);
        var catalog = File.Exists(fullPath)
            ? BenchmarkJson.Read<BenchmarkEvidenceCatalog>(fullPath)
            : null;
        var updated = Update(catalog, result, comparisonId, resultPath, runMode, publish, expectedPlatforms, platform);
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
        AddCompatibilityDimension(dimensions, "environment.runtimeVersion", result.Environment.RuntimeVersion);
        AddCompatibilityDimension(dimensions, "environment.dotNetSdkVersion", result.Environment.DotNetSdkVersion);
        AddCompatibilityDimension(dimensions, "environment.runner", result.Environment.Runner);
        AddCompatibilityDimension(
            dimensions,
            "benchmark.workload.shape.sha256",
            ComputeWorkloadShape(result));
        AddCompatibilityDimension(
            dimensions,
            "benchmark.comparison.shape.sha256",
            ComputeComparisonShape(result));
        foreach (var item in result.Metadata.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Key.StartsWith("benchmark.fixture.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.package.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.workload.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.execution.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("benchmark.runner.", StringComparison.OrdinalIgnoreCase) ||
                ExecutionPolicyMetadataKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase) ||
                item.Key.Equals("gitSha", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("psEdition", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("powerShellVersion", StringComparison.OrdinalIgnoreCase))
            {
                dimensions[item.Key] = item.Value;
            }
        }

        return dimensions;
    }

    private static string ComputeWorkloadShape(BenchmarkRunResult result)
    {
        string[] identities = result.Samples
            .Select(sample => CreateWorkloadIdentity(
                sample.Scenario,
                sample.Operation,
                sample.Engine,
                sample.Host,
                sample.Variables))
            .Concat(result.Summary.Select(row => CreateWorkloadIdentity(
                row.Scenario,
                row.Operation,
                row.Engine,
                row.Host,
                row.Variables)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (identities.Length == 0)
            return string.Empty;

        string canonical = string.Join("\n", identities);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ComputeComparisonShape(BenchmarkRunResult result)
    {
        string[] identities = result.Comparison
            .Select(row =>
            {
                var builder = new StringBuilder();
                AppendIdentityPart(builder, "scenario", row.Scenario);
                AppendIdentityPart(builder, "operation", row.Operation);
                AppendIdentityPart(builder, "host", row.Host);
                AppendIdentityPart(builder, "runMode", row.RunMode);
                AppendIdentityPart(builder, "engine", row.Engine);
                AppendIdentityPart(builder, "baselineEngine", row.BaselineEngine);
                AppendIdentityPart(builder, "metric", row.Metric);
                AppendIdentityPart(
                    builder,
                    "tieTolerance",
                    row.TieTolerance.ToString("R", CultureInfo.InvariantCulture));
                foreach (var variable in row.Variables
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    AppendIdentityPart(
                        builder,
                        variable.Key.ToUpperInvariant(),
                        variable.Value);
                }

                return builder.ToString();
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (identities.Length == 0)
            return string.Empty;

        string canonical = string.Join("\n", identities);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string CreateWorkloadIdentity(
        string scenario,
        string operation,
        string engine,
        string host,
        IReadOnlyDictionary<string, string?> variables)
    {
        var builder = new StringBuilder();
        AppendIdentityPart(builder, "scenario", scenario);
        AppendIdentityPart(builder, "operation", operation);
        AppendIdentityPart(builder, "engine", engine);
        AppendIdentityPart(builder, "host", host);
        foreach (var variable in variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            AppendIdentityPart(builder, variable.Key.ToUpperInvariant(), variable.Value);
        }

        return builder.ToString();
    }

    private static void AppendIdentityPart(StringBuilder builder, string key, string? value)
    {
        string text = value ?? "<null>";
        builder.Append(key.Length)
            .Append(':')
            .Append(key)
            .Append('=')
            .Append(text.Length)
            .Append(':')
            .Append(text)
            .Append(';');
    }

    private static void AddCompatibilityDimension(
        IDictionary<string, string> dimensions,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            dimensions[key] = value!.Trim();
    }

    private static string ResolvePlatform(BenchmarkRunResult result, string? platformOverride)
    {
        var labels = new[] { platformOverride, result.Environment.OsFamily }
            .Concat(MetadataValues(result.Metadata, "osLabel", "os"))
            .Concat(result.Samples.Select(sample => sample.Os))
            .Concat(result.Summary.Select(row => row.Os))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(BenchmarkPlatformNormalizer.NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (labels.Length == 0)
        {
            throw new InvalidOperationException(
                "The benchmark result does not identify its operating-system platform. Supply the producing platform explicitly.");
        }

        if (labels.Length > 1)
        {
            throw new InvalidOperationException(
                $"Benchmark evidence must identify one operating-system platform; found {string.Join(", ", labels)}.");
        }

        return labels[0];
    }

    private static void ApplyCompatibility(BenchmarkEvidenceEntry[] entries)
    {
        foreach (var group in entries.GroupBy(
                     entry => string.Join("\u001f", entry.ComparisonId, entry.RunMode),
                     StringComparer.OrdinalIgnoreCase))
        {
            BenchmarkEvidenceEntry[] published = group.Where(entry => entry.Publish).ToArray();
            foreach (var entry in group)
            {
                BenchmarkEvidenceEntry[] candidates = published.Length == 0
                    ? group.ToArray()
                    : entry.Publish
                        ? published
                        : published.Append(entry).ToArray();
                string[] issues = CompareDimensions(candidates);
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
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (values.Length > 1)
                issues.Add($"{key}: lanes contain incompatible values ({string.Join(", ", values.Select(value => $"'{value}'"))}).");
        }

        return issues.ToArray();
    }

    private static void ValidatePublishableResult(BenchmarkRunResult result)
    {
        string? gitSha = MetadataValue(result.Metadata, "gitSha");
        if (!IsFullGitObjectId(gitSha))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires exact source provenance in metadata key 'gitSha' " +
                "as a full 40- or 64-character hexadecimal Git object ID.");
        }

        if (!string.Equals(
                MetadataValue(result.Metadata, "gitWorktreeClean"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires metadata key 'gitWorktreeClean' to be true. " +
                "Commit or remove all tracked and untracked source changes before measuring.");
        }

        bool hasUnknownStatus =
            result.Samples.Any(sample =>
                !Enum.IsDefined(typeof(BenchmarkSampleStatus), sample.Status)) ||
            result.Summary.Any(row => !IsKnownSummaryStatus(row.Status));
        if (hasUnknownStatus)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence contains an unknown measurement status. " +
                "Only Succeeded, Failed, and Skipped statuses are supported.");
        }

        bool hasSkippedMeasurement =
            result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Skipped) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Skipped", StringComparison.OrdinalIgnoreCase));
        if (hasSkippedMeasurement)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence cannot contain skipped measurements. " +
                "Use a diagnostic, non-publishable lane for incomplete work.");
        }

        bool hasFailedMeasurement =
            result.Samples.Any(sample =>
                sample.Status == BenchmarkSampleStatus.Failed ||
                (sample.Status == BenchmarkSampleStatus.Succeeded && !IsValidDuration(sample.DurationMs))) ||
            result.Summary.Any(row =>
                row.FailureCount > 0 ||
                string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                 !IsValidDuration(row.MedianMs) &&
                 !IsValidDuration(row.MeanMs)));
        bool hasSuccessfulMeasurement =
            result.Samples.Any(sample =>
                sample.Status == BenchmarkSampleStatus.Succeeded &&
                IsValidDuration(sample.DurationMs)) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                row.SampleCount > 0 &&
                (IsValidDuration(row.MedianMs) || IsValidDuration(row.MeanMs)));
        if (hasFailedMeasurement || !hasSuccessfulMeasurement)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires at least one successful measurement and no failed measurements.");
        }
    }

    private static void ValidateCatalogSchema(BenchmarkEvidenceCatalog? catalog)
    {
        if (catalog is null)
            return;
        if (catalog.SchemaVersion is 1 or SupportedSchemaVersion)
            return;

        throw new InvalidOperationException(
            $"Benchmark evidence catalog schema {catalog.SchemaVersion} is not supported by this build. " +
            $"Supported schemas are 1 and {SupportedSchemaVersion}; use a compatible PowerForge version before updating the catalog.");
    }

    private static void ValidateEmbeddedRunModes(BenchmarkRunResult result, string expectedRunMode)
    {
        var modes = new List<string>();
        foreach (var item in result.Metadata)
        {
            if (item.Key.Equals("runMode", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("benchmark.runMode", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals("benchmark.execution.runMode", StringComparison.OrdinalIgnoreCase))
            {
                AddRunMode(modes, item.Value);
            }
        }
        foreach (BenchmarkSample sample in result.Samples)
            AddRunMode(modes, sample.RunMode);
        foreach (BenchmarkSummaryRow row in result.Summary)
            AddRunMode(modes, row.RunMode);
        foreach (BenchmarkComparisonRow row in result.Comparison)
            AddRunMode(modes, row.RunMode);

        string[] conflicting = modes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(mode => !string.Equals(mode, expectedRunMode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mode => mode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (conflicting.Length == 0)
            return;

        throw new InvalidOperationException(
            $"Publishable benchmark evidence declares embedded run mode(s) {string.Join(", ", conflicting.Select(mode => $"'{mode}'"))}, " +
            $"which conflict with requested run mode '{expectedRunMode}'.");
    }

    private static void AddRunMode(ICollection<string> modes, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string normalized = value!.Trim().ToLowerInvariant();
        if (!string.Equals(normalized, "import", StringComparison.Ordinal))
            modes.Add(normalized);
    }

    private static bool IsValidDuration(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsValidDuration(double? value)
        => value.HasValue && IsValidDuration(value.Value);

    private static bool IsKnownSummaryStatus(string? status)
        => string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "Skipped", StringComparison.OrdinalIgnoreCase);

    private static bool IsFullGitObjectId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidate = value!.Trim();
        return (candidate.Length == 40 || candidate.Length == 64) &&
               candidate.All(Uri.IsHexDigit);
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

    private static IEnumerable<string> MetadataValues(
        IReadOnlyDictionary<string, string> metadata,
        params string[] keys)
    {
        foreach (var item in metadata)
        {
            if (keys.Any(key =>
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                yield return item.Value;
            }
        }
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
