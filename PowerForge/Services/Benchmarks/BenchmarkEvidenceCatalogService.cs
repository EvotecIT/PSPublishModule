using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Builds and validates a platform-aware benchmark evidence catalog.
/// </summary>
public sealed partial class BenchmarkEvidenceCatalogService
{
    private const int SupportedSchemaVersion = 3;
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
        if (catalog.SchemaVersion == 1)
        {
            foreach (BenchmarkEvidenceEntry legacyEntry in catalog.Entries)
                legacyEntry.Publish = false;
        }
        catalog.SchemaVersion = SupportedSchemaVersion;
        catalog.ExpectedPlatforms = NormalizeExpectedPlatforms(expectedPlatforms ?? catalog.ExpectedPlatforms);

        string resolvedPlatform = ResolvePlatform(result, platform);
        DateTimeOffset generatedUtc = ResolveGeneratedUtc(result);

        var entry = new BenchmarkEvidenceEntry
        {
            ComparisonId = comparisonId.Trim(),
            Platform = resolvedPlatform,
            RunMode = normalizedRunMode,
            GeneratedUtc = generatedUtc,
            Publish = publish,
            ResultPath = resultPath,
            ResultSha256 = BenchmarkJson.ComputeSha256(result),
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
        ValidateDistinctPublishedResultPaths(entries);

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
    /// <param name="resultArtifactPath">
    /// Optional local filesystem destination for the normalized result. Use this when
    /// <paramref name="resultPath"/> is a website URL or another portable consumer path.
    /// </param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog UpdateFile(
        string catalogPath,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null,
        string? resultArtifactPath = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath)) throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        string fullPath = BenchmarkJson.ResolveWritePath(catalogPath);
        using var fileLease = BenchmarkFileUpdateLock.Acquire(fullPath);
        var catalog = File.Exists(fullPath)
            ? BenchmarkJson.Read<BenchmarkEvidenceCatalog>(fullPath)
            : null;
        var updated = Update(catalog, result, comparisonId, resultPath, runMode, publish, expectedPlatforms, platform);
        string? artifactPath = null;
        byte[]? previousArtifact = null;
        bool artifactExisted = false;
        bool artifactSelected = false;
        BenchmarkEvidenceEntry writtenEntry = updated.Entries.Single(entry =>
            string.Equals(entry.ComparisonId, comparisonId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Platform, ResolvePlatform(result, platform), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.RunMode, runMode.Trim(), StringComparison.OrdinalIgnoreCase));
        string inputHash = BenchmarkJson.ComputeSha256(result);
        bool inputWasSelected = string.Equals(
                                    writtenEntry.ResultSha256,
                                    inputHash,
                                    StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(
                                    writtenEntry.ResultPath,
                                    resultPath,
                                    StringComparison.Ordinal);
        if (inputWasSelected)
        {
            artifactPath = ResolveResultArtifactPath(
                fullPath,
                resultPath,
                resultArtifactPath);
            if (string.Equals(artifactPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The benchmark result artifact path must be different from the evidence catalog path.");
            }
            string artifactDestinationSha256 =
                ComputeArtifactDestinationSha256(fullPath, artifactPath);
            ValidateArtifactDestinationOwnership(
                catalog,
                fullPath,
                artifactPath,
                artifactDestinationSha256,
                writtenEntry);
            writtenEntry.ArtifactDestinationSha256 = artifactDestinationSha256;
            writtenEntry.ArtifactFileName = Path.GetFileName(artifactPath);
            artifactSelected = true;
            artifactExisted = File.Exists(artifactPath);
            if (artifactExisted)
                previousArtifact = File.ReadAllBytes(artifactPath);
        }

        try
        {
            if (artifactSelected)
            {
                BenchmarkJson.Write(artifactPath!, result);
                string writtenHash = BenchmarkJson.ComputeFileSha256(artifactPath!);
                if (!string.Equals(writtenEntry.ResultSha256, writtenHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The normalized benchmark result artifact does not match the validated catalog payload.");
                }
            }

            BenchmarkJson.Write(fullPath, updated);
            return updated;
        }
        catch (Exception publicationException)
        {
            if (!artifactSelected)
                throw;
            try
            {
                if (artifactExisted)
                    BenchmarkJson.WriteBytes(artifactPath!, previousArtifact!);
                else if (File.Exists(artifactPath))
                    File.Delete(artifactPath);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Benchmark evidence publication failed and the previous result artifact could not be restored.",
                    new AggregateException(publicationException, rollbackException));
            }

            throw;
        }
    }

    private static string ResolveResultArtifactPath(
        string catalogPath,
        string resultPath,
        string? resultArtifactPath)
    {
        if (!string.IsNullOrWhiteSpace(resultArtifactPath))
            return BenchmarkJson.ResolveWritePath(resultArtifactPath!);
        if (Path.IsPathRooted(resultPath))
            return BenchmarkJson.ResolveWritePath(resultPath);
        if (Uri.TryCreate(resultPath, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile)
            {
                throw new InvalidOperationException(
                    "Benchmark evidence must use a local result artifact path so PowerForge can write and hash the normalized payload.");
            }

            return BenchmarkJson.ResolveWritePath(uri.LocalPath);
        }

        string catalogDirectory = Path.GetDirectoryName(catalogPath)
                                  ?? throw new InvalidOperationException(
                                      $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        return BenchmarkJson.ResolveWritePath(Path.Combine(catalogDirectory, resultPath));
    }

    private static void ValidateArtifactDestinationOwnership(
        BenchmarkEvidenceCatalog? catalog,
        string catalogPath,
        string artifactPath,
        string artifactDestinationSha256,
        BenchmarkEvidenceEntry selectedEntry)
    {
        if (catalog is null)
            return;

        string? existingArtifactHash = File.Exists(artifactPath)
            ? BenchmarkJson.ComputeFileSha256(artifactPath)
            : null;
        foreach (BenchmarkEvidenceEntry existing in catalog.Entries)
        {
            if (SameLane(existing, selectedEntry))
                continue;

            bool persistedDestinationMatches =
                !string.IsNullOrWhiteSpace(existing.ArtifactDestinationSha256) &&
                string.Equals(
                    existing.ArtifactDestinationSha256,
                    artifactDestinationSha256,
                    StringComparison.OrdinalIgnoreCase);
            bool resolvedPathMatches =
                TryResolveExistingArtifactPath(
                    catalogPath,
                    existing.ResultPath,
                    out string? existingArtifactPath) &&
                string.Equals(
                    existingArtifactPath,
                    artifactPath,
                    FrameworkCompatibility.GetPathStringComparison(artifactPath));
            bool existingContentOwnsDestination =
                string.IsNullOrWhiteSpace(existing.ArtifactDestinationSha256) &&
                !string.IsNullOrWhiteSpace(existingArtifactHash) &&
                !string.IsNullOrWhiteSpace(existing.ResultSha256) &&
                string.Equals(
                    existingArtifactHash,
                    existing.ResultSha256,
                    StringComparison.OrdinalIgnoreCase);
            if (!persistedDestinationMatches &&
                !resolvedPathMatches &&
                !existingContentOwnsDestination)
                continue;

            throw new InvalidOperationException(
                $"Published benchmark lane '{selectedEntry.ComparisonId}/{selectedEntry.Platform}/{selectedEntry.RunMode}' " +
                $"resolves to the artifact destination already owned by " +
                $"'{existing.ComparisonId}/{existing.Platform}/{existing.RunMode}': '{artifactPath}'.");
        }
    }

    private static string ComputeArtifactDestinationSha256(
        string catalogPath,
        string artifactPath)
    {
        string catalogDirectory = Path.GetDirectoryName(catalogPath)
                                  ?? throw new InvalidOperationException(
                                      $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        string normalizedDestination = FrameworkCompatibility.GetRelativePath(
                catalogDirectory,
                artifactPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (FrameworkCompatibility.GetPathStringComparison(artifactPath) ==
            StringComparison.OrdinalIgnoreCase)
        {
            normalizedDestination = normalizedDestination.ToLowerInvariant();
        }
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedDestination));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static bool TryResolveExistingArtifactPath(
        string catalogPath,
        string resultPath,
        out string? artifactPath)
    {
        try
        {
            artifactPath = ResolveResultArtifactPath(
                catalogPath,
                resultPath,
                resultArtifactPath: null);
            return true;
        }
        catch (InvalidOperationException)
        {
            artifactPath = null;
            return false;
        }
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
        AddCompatibilityDimension(dimensions, "environment.osArchitecture", result.Environment.OsArchitecture);
        AddCompatibilityDimension(dimensions, "environment.processArchitecture", result.Environment.ProcessArchitecture);
        AddCompatibilityDimension(dimensions, "environment.processorName", result.Environment.ProcessorName);
        AddCompatibilityDimension(
            dimensions,
            "environment.physicalProcessorCount",
            result.Environment.PhysicalProcessorCount?.ToString(CultureInfo.InvariantCulture));
        AddCompatibilityDimension(
            dimensions,
            "environment.physicalCoreCount",
            result.Environment.PhysicalCoreCount?.ToString(CultureInfo.InvariantCulture));
        AddCompatibilityDimension(
            dimensions,
            "environment.logicalCoreCount",
            result.Environment.LogicalCoreCount?.ToString(CultureInfo.InvariantCulture));
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
            if (BenchmarkEvidenceMetadataPolicy.IsCompatibilityKey(item.Key))
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

    private static void ValidatePublishableResult(
        BenchmarkRunResult result,
        bool requireValidatedImportProvenance = true)
    {
        if (requireValidatedImportProvenance &&
            !string.IsNullOrWhiteSpace(MetadataValue(result.Metadata, "importedUtc")) &&
            !result.HasValidatedProductionProvenance)
        {
            throw new InvalidOperationException(
                "Publishable imported benchmark evidence requires a production provenance sidecar captured around the benchmark run.");
        }
        if (result.HasValidatedProductionProvenance &&
            (!BenchmarkResultImporter.HasUnchangedValidatedProductionMetadata(result) ||
             !string.Equals(
                 result.ValidatedProductionContentSha256,
                 BenchmarkResultImporter.ComputeValidatedProductionContentSha256(result),
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Imported benchmark measurements changed after production provenance validation. " +
                "Re-import the original sidecar-bound artifacts before publication.");
        }
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

        if (string.IsNullOrWhiteSpace(result.Environment.RuntimeVersion) ||
            string.IsNullOrWhiteSpace(result.Environment.Runner))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires the measured runtime identity and benchmark runner identity.");
        }
        if (string.IsNullOrWhiteSpace(result.Environment.ProcessArchitecture) ||
            IsGenericProcessorIdentity(result.Environment.ProcessorName))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires processArchitecture and a specific processorName hardware identity.");
        }

        bool hasUnknownStatus =
            result.Samples.Any(sample =>
                !Enum.IsDefined(typeof(BenchmarkSampleStatus), sample.Status)) ||
            result.Summary.Any(row => !IsKnownMeasurementStatus(row.Status)) ||
            result.Comparison.Any(row => !IsKnownMeasurementStatus(row.Status));
        if (hasUnknownStatus)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence contains an unknown measurement status. " +
                "Only Succeeded, Failed, and Skipped statuses are supported.");
        }

        bool hasSkippedMeasurement =
            result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Skipped) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Skipped", StringComparison.OrdinalIgnoreCase)) ||
            result.Comparison.Any(row =>
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
                 (row.SampleCount <= 0 ||
                  (!IsValidDuration(row.MedianMs) &&
                   !IsValidDuration(row.MeanMs))))) ||
            result.Comparison.Any(row =>
                string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                 !IsValidComparison(row)));
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

        ValidateSuiteConsistency(result);
        ValidateSummariesMatchSamples(result);
        ValidateSummaryStatistics(result.Summary);
        ValidateComparisonsMatchSummaries(result);
    }

    private static DateTimeOffset ResolveGeneratedUtc(BenchmarkRunResult result)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset generatedUtc =
            result.FinishedUtc == default ? now : result.FinishedUtc;
        if (generatedUtc > now.AddMinutes(5))
        {
            throw new InvalidOperationException(
                "Benchmark completion time cannot be in the future beyond the allowed five-minute clock-skew tolerance.");
        }
        if (result.StartedUtc != default && generatedUtc < result.StartedUtc)
        {
            throw new InvalidOperationException(
                "Benchmark completion time cannot precede its start time.");
        }

        return generatedUtc;
    }

    private static bool IsGenericProcessorIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string normalized = value!.Trim();
        return normalized.Equals("processor", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(" processor", StringComparison.OrdinalIgnoreCase) &&
               Enum.GetNames(typeof(System.Runtime.InteropServices.Architecture))
                   .Any(architecture => normalized.Equals(
                       architecture + " processor",
                       StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateDistinctPublishedResultPaths(
        IReadOnlyCollection<BenchmarkEvidenceEntry> entries)
    {
        string[] duplicates = entries
            .Where(entry => entry.Publish)
            .GroupBy(entry => entry.ResultPath, StringComparer.Ordinal)
            .Where(group => group
                .Select(entry => string.Join(
                    "\u001f",
                    entry.ComparisonId,
                    entry.Platform,
                    entry.RunMode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length == 0)
            return;

        throw new InvalidOperationException(
            "Published benchmark lanes must use distinct result paths. Shared path(s): " +
            string.Join(", ", duplicates.Select(path => $"'{path}'")) + ".");
    }

    private static void ValidateSuiteConsistency(BenchmarkRunResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Suite))
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires a non-empty top-level suite identity.");
        }

        string suite = result.Suite.Trim();
        bool mismatch = result.Samples.Any(sample =>
                            !string.Equals(sample.Suite?.Trim(), suite, StringComparison.Ordinal)) ||
                        result.Summary.Any(row =>
                            !string.Equals(row.Suite?.Trim(), suite, StringComparison.Ordinal)) ||
                        result.Comparison.Any(row =>
                            !string.Equals(row.Suite?.Trim(), suite, StringComparison.Ordinal));
        if (mismatch)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires the top-level suite to match every sample, summary, and comparison row.");
        }
    }

    private static void ValidateCatalogSchema(BenchmarkEvidenceCatalog? catalog)
    {
        if (catalog is null)
            return;
        if (catalog.SchemaVersion is 1 or 2 or SupportedSchemaVersion)
            return;

        throw new InvalidOperationException(
            $"Benchmark evidence catalog schema {catalog.SchemaVersion} is not supported by this build. " +
            $"Supported schemas are 1, 2, and {SupportedSchemaVersion}; use a compatible PowerForge version before updating the catalog.");
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

    private static bool IsKnownMeasurementStatus(string? status)
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
