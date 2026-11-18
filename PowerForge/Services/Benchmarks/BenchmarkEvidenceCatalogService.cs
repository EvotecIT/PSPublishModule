using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Builds and validates a platform-aware benchmark evidence catalog.
/// </summary>
public sealed partial class BenchmarkEvidenceCatalogService
{
    /// <summary>The only catalog and declaration schema this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 5;
    private static readonly string[] DefaultExpectedPlatforms = { "windows", "linux", "macos" };

    /// <summary>
    /// Adds or replaces one lane and recomputes comparison and availability state.
    /// </summary>
    /// <param name="catalog">Existing catalog, or <see langword="null"/> for a new catalog.</param>
    /// <param name="result">Normalized benchmark result.</param>
    /// <param name="comparisonId">Stable identifier for one equivalent workload definition.</param>
    /// <param name="resultPath">Portable path or URL to the normalized run result.</param>
    /// <param name="profile">Evidence profile: diagnostic, standard or full.</param>
    /// <param name="publish">Whether the lane supports public benchmark claims.</param>
    /// <param name="expectedPlatforms">Platforms expected for complete evidence.</param>
    /// <param name="platform">Producing platform override for artifacts that do not carry OS metadata.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog Update(
        BenchmarkEvidenceCatalog? catalog,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string profile,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null)
        => Update(catalog, new BenchmarkEvidenceUpdateRequest
        {
            Result = result,
            ComparisonId = comparisonId,
            ResultPath = resultPath,
            Profile = ParseProfile(profile),
            Publish = publish,
            ExpectedPlatforms = expectedPlatforms?.ToArray(),
            Platform = platform
        });

    /// <summary>
    /// Adds or replaces one lane. The lane key is comparison, platform, framework, profile and lane parameters;
    /// a replaced lane is kept in <see cref="BenchmarkEvidenceCatalog.Superseded"/> with its date and hash.
    /// </summary>
    /// <param name="catalog">Existing catalog, or <see langword="null"/> for a new catalog.</param>
    /// <param name="request">Update request.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog Update(BenchmarkEvidenceCatalog? catalog, BenchmarkEvidenceUpdateRequest request)
        => UpdateCore(catalog, request, out _);

    private BenchmarkEvidenceCatalog UpdateCore(
        BenchmarkEvidenceCatalog? catalog,
        BenchmarkEvidenceUpdateRequest request,
        out BenchmarkRunResult payload)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        BenchmarkRunResult result = request.Result ?? throw new ArgumentException("Benchmark result is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ResultPath)) throw new ArgumentException("Result path is required.", nameof(request));
        string? idProblem = BenchmarkEvidenceIdentity.DescribeComparisonIdProblem(request.ComparisonId);
        if (idProblem is not null) throw new ArgumentException(idProblem, nameof(request));
        string normalizedComparisonId = request.ComparisonId.Trim();
        string profileName = FormatProfile(request.Profile);

        ValidateCatalogSchema(catalog);
        // Rules and lanes are staged on a copy; the caller's catalog changes only through the returned value.
        catalog = catalog is null
            ? new BenchmarkEvidenceCatalog()
            : BenchmarkJson.ReadBytes<BenchmarkEvidenceCatalog>(BenchmarkJson.SerializeCanonicalBytes(catalog), "benchmark evidence catalog");
        if (request.Declarations is not null)
            ApplyDeclarationDocument(catalog, request.Declarations);
        if (request.Policy is not null)
            catalog.Policy = request.Policy;
        catalog.SchemaVersion = CurrentSchemaVersion;
        catalog.ExpectedPlatforms = NormalizeExpectedPlatforms(request.ExpectedPlatforms ?? catalog.ExpectedPlatforms);

        if (request.Publish && !catalog.Policy.PublishableProfiles.Contains(request.Profile))
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{normalizedComparisonId}' does not meet the publish policy: The {profileName} profile is never published.");
        }
        if (request.Publish)
        {
            ValidateEmbeddedRunModes(result, profileName);
            ValidatePublishableResult(result);
            ValidateBoundPublishIdentity(result, normalizedComparisonId, profileName);
        }

        string resolvedPlatform = ResolvePlatform(result, request.Platform);
        DateTimeOffset generatedUtc = ResolveGeneratedUtc(result);
        string framework = BenchmarkEvidenceIdentity.ResolveFramework(result, request.Framework);
        Dictionary<string, string> parameters = ResolveLaneParameters(result, request.Parameters);
        BenchmarkPolicyEvaluation evaluation = Evaluate(catalog, result, normalizedComparisonId, framework, request.Profile, request.Publish);
        if (request.Publish && evaluation.LaneIssues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{normalizedComparisonId}' does not meet the publish policy: " +
                string.Join(" ", evaluation.LaneIssues));
        }

        // A published lane carries no measurements of libraries whose licence class is not published: their rows
        // are removed from the stored result and their cells stay only as "not published (licence)" markers.
        payload = request.Publish ? RedactLicenceRestricted(result, evaluation.Cells) : result;
        var entry = new BenchmarkEvidenceEntry
        {
            ComparisonId = normalizedComparisonId,
            Platform = resolvedPlatform,
            Framework = framework,
            Profile = request.Profile,
            Parameters = parameters,
            LaneKey = BenchmarkEvidenceIdentity.CreateLaneKey(normalizedComparisonId, resolvedPlatform, framework, request.Profile, parameters),
            GeneratedUtc = generatedUtc,
            Publish = request.Publish,
            ResultSha256 = BenchmarkJson.ComputeSha256(payload),
            Suite = payload.Suite,
            Environment = CopyEnvironment(payload.Environment),
            Compatibility = BuildCompatibility(payload)
        };
        // Result paths are content-addressed so a newer run never overwrites the artifact of a superseded one.
        entry.ResultPath = CreateContentAddressedResultPath(request.ResultPath, entry.ResultSha256);
        ApplyEvaluation(entry, evaluation);

        BenchmarkEvidenceEntry? existingLane = catalog.Entries
            .Where(existing => SameLane(existing, entry))
            .OrderByDescending(existing => existing.GeneratedUtc)
            .FirstOrDefault();
        BenchmarkEvidenceEntry selectedEntry = existingLane is not null &&
                                               existingLane.GeneratedUtc > entry.GeneratedUtc
            ? existingLane
            : entry;
        BenchmarkEvidenceEntry? replaced = existingLane is null
            ? null
            : ReferenceEquals(selectedEntry, entry) ? existingLane : entry;
        var superseded = catalog.Superseded.ToList();
        if (replaced is not null &&
            !string.Equals(replaced.ResultSha256, selectedEntry.ResultSha256, StringComparison.OrdinalIgnoreCase))
        {
            superseded.Add(CreateSuperseded(replaced, selectedEntry.GeneratedUtc));
        }

        var entries = catalog.Entries
            .Where(existing => !SameLane(existing, entry))
            .Append(selectedEntry)
            .OrderBy(existing => existing.LaneKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ValidateDistinctPublishedResultPaths(entries);

        catalog.Entries = entries;
        ReevaluateRetainedLanes(catalog, ReferenceEquals(selectedEntry, entry) ? entry : null);
        ApplyCompatibility(entries);
        catalog.Superseded = NormalizeSuperseded(superseded, entries);
        ApplyDefinitions(catalog);
        return catalog;
    }

    /// <summary>
    /// Reads an existing catalog, updates it, and writes the new JSON atomically through the common serializer.
    /// </summary>
    /// <param name="catalogPath">Catalog JSON path.</param>
    /// <param name="result">Normalized benchmark result.</param>
    /// <param name="comparisonId">Stable identifier for one equivalent workload definition.</param>
    /// <param name="resultPath">Portable path or URL to the normalized run result.</param>
    /// <param name="profile">Evidence profile: diagnostic, standard or full.</param>
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
        string profile,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null,
        string? resultArtifactPath = null)
        => UpdateFile(catalogPath, new BenchmarkEvidenceUpdateRequest
        {
            Result = result,
            ComparisonId = comparisonId,
            ResultPath = resultPath,
            Profile = ParseProfile(profile),
            Publish = publish,
            ExpectedPlatforms = expectedPlatforms?.ToArray(),
            Platform = platform,
            ResultArtifactPath = resultArtifactPath
        });

    /// <summary>
    /// Reads an existing catalog, updates it, and writes the catalog and the normalized result artifact atomically.
    /// Call it once per comparison as results arrive so a later failure never loses earlier evidence.
    /// </summary>
    /// <param name="catalogPath">Catalog JSON path.</param>
    /// <param name="request">Update request.</param>
    /// <returns>Updated catalog.</returns>
    public BenchmarkEvidenceCatalog UpdateFile(string catalogPath, BenchmarkEvidenceUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(catalogPath)) throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        if (request is null) throw new ArgumentNullException(nameof(request));
        BenchmarkRunResult result = request.Result ?? throw new ArgumentException("Benchmark result is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ResultPath)) throw new ArgumentException("Result path is required.", nameof(request));
        if (result.FinishedUtc == default)
        {
            if (result.HasValidatedProductionProvenance)
            {
                throw new InvalidOperationException(
                    "Validated production benchmark evidence must preserve its sidecar-bound completion timestamp.");
            }
            result.FinishedUtc = ResolveGeneratedUtc(result);
        }
        string fullPath = BenchmarkJson.ResolveWritePath(catalogPath);
        using var fileLease = BenchmarkFileUpdateLock.Acquire(fullPath);
        var catalog = File.Exists(fullPath)
            ? ReadCatalog(fullPath)
            : null;
        var updated = UpdateCore(catalog, request, out BenchmarkRunResult payload);
        string? artifactPath = null;
        byte[]? previousArtifact = null;
        bool artifactExisted = false;
        bool artifactSelected = false;
        string laneKey = BenchmarkEvidenceIdentity.CreateLaneKey(
            request.ComparisonId.Trim(),
            ResolvePlatform(result, request.Platform),
            BenchmarkEvidenceIdentity.ResolveFramework(result, request.Framework),
            request.Profile,
            ResolveLaneParameters(result, request.Parameters));
        BenchmarkEvidenceEntry writtenEntry = updated.Entries.Single(entry =>
            string.Equals(entry.LaneKey, laneKey, StringComparison.OrdinalIgnoreCase));
        string inputHash = BenchmarkJson.ComputeSha256(payload);
        string resultPath = CreateContentAddressedResultPath(request.ResultPath, inputHash);
        bool inputWasSelected = string.Equals(
                                    writtenEntry.ResultSha256,
                                    inputHash,
                                    StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(
                                    writtenEntry.ResultPath,
                                    resultPath,
                                    StringComparison.Ordinal);
        // The incoming result is always written: as the lane's current run, or as a dated superseded run when a
        // newer run already holds the lane. Content addressing means it never replaces another run's file.
        artifactPath = ContentAddressedFilePath(
            ResolveResultArtifactPath(fullPath, resultPath, request.ResultArtifactPath),
            inputHash);
        if (string.Equals(artifactPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The benchmark result artifact path must be different from the evidence catalog path.");
        }
        artifactSelected = true;
        artifactExisted = File.Exists(artifactPath);
        if (artifactExisted)
            previousArtifact = File.ReadAllBytes(artifactPath);
        if (inputWasSelected)
        {
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
            writtenEntry.ArtifactRelativePath = ResolveArtifactRelativePath(
                fullPath,
                artifactPath,
                resultPath);
        }

        try
        {
            if (artifactSelected)
            {
                BenchmarkJson.Write(artifactPath!, payload);
                string writtenHash = BenchmarkJson.ComputeFileSha256(artifactPath!);
                if (!string.Equals(inputHash, writtenHash, StringComparison.OrdinalIgnoreCase))
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

    private static string[] NormalizeExpectedPlatforms(IEnumerable<string>? platforms)
    {
        var normalized = (platforms ?? DefaultExpectedPlatforms)
            .Select(BenchmarkPlatformNormalizer.NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? DefaultExpectedPlatforms.ToArray() : normalized;
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

        // Failed and skipped cells are recorded and excluded per cell by the publish policy, so one
        // crashed library never discards the rest of a run. Succeeded rows must still be well formed.
        bool hasMalformedMeasurement =
            result.Samples.Any(sample =>
                sample.Status == BenchmarkSampleStatus.Succeeded && !IsValidDuration(sample.DurationMs)) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                (row.SampleCount <= 0 ||
                 (!IsValidDuration(row.MedianMs) && !IsValidDuration(row.MeanMs)))) ||
            result.Comparison.Any(row =>
                string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                !IsValidComparison(row) &&
                // A failed or skipped baseline leaves no baseline value or ratio; the rows are recomputed from the
                // summaries below, so only that exact shape is accepted.
                !(IsFinite(row.Actual) && row.Baseline is null && row.Ratio is null));
        bool hasSuccessfulMeasurement =
            result.Samples.Any(sample =>
                sample.Status == BenchmarkSampleStatus.Succeeded &&
                IsValidDuration(sample.DurationMs)) ||
            result.Summary.Any(row =>
                string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                row.SampleCount > 0 &&
                (IsValidDuration(row.MedianMs) || IsValidDuration(row.MeanMs)));
        if (hasMalformedMeasurement || !hasSuccessfulMeasurement)
        {
            throw new InvalidOperationException(
                "Publishable benchmark evidence requires at least one successful measurement, and every successful measurement needs a valid duration.");
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
                .Select(entry => entry.LaneKey)
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
        if (catalog is null || catalog.SchemaVersion == CurrentSchemaVersion)
            return;

        throw new InvalidOperationException(
            $"Benchmark evidence catalog schema {catalog.SchemaVersion} is not supported. Only schema {CurrentSchemaVersion} is read; " +
            "older catalogs are not converted. Record fresh runs into a new schema 5 catalog.");
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

        if (result.HasValidatedProductionProvenance && modes.Count == 0)
        {
            throw new InvalidOperationException(
                "Publishable production benchmark evidence requires an explicit run mode bound into the provenance sidecar or measured artifact.");
        }

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

    private static void ValidateBoundPublishIdentity(
        BenchmarkRunResult result,
        string comparisonId,
        string runMode)
    {
        bool sidecarBound =
            result.HasValidatedProductionProvenance ||
            string.Equals(
                MetadataValue(result.Metadata, "benchmark.provenance.source"),
                "sidecar",
                StringComparison.OrdinalIgnoreCase);
        if (!sidecarBound)
            return;

        string? boundRunMode = MetadataValue(
            result.Metadata,
            "benchmark.provenance.runMode");
        if (string.IsNullOrWhiteSpace(boundRunMode))
        {
            throw new InvalidOperationException(
                "Publishable production benchmark evidence requires a run mode bound into its provenance sidecar before measurement.");
        }
        if (!string.Equals(boundRunMode, runMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Publishable benchmark run mode '{runMode}' must exactly match the provenance-bound run mode '{boundRunMode}'.");
        }

        string? workloadId = MetadataValue(result.Metadata, "benchmark.workload.id");
        if (string.IsNullOrWhiteSpace(workloadId))
        {
            throw new InvalidOperationException(
                "Publishable production benchmark evidence requires metadata key 'benchmark.workload.id' to be bound before measurement.");
        }
        if (!string.Equals(workloadId, comparisonId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Publishable benchmark comparison ID '{comparisonId}' must exactly match the provenance-bound workload ID '{workloadId}'.");
        }
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

    private static bool SameLane(BenchmarkEvidenceEntry left, BenchmarkEvidenceEntry right)
        => string.Equals(left.LaneKey, right.LaneKey, StringComparison.OrdinalIgnoreCase);

    private static string CompatibilityGroup(BenchmarkEvidenceEntry entry)
        => BenchmarkEvidenceIdentity.CreateLaneKey(entry.ComparisonId, "*", entry.Framework, entry.Profile, entry.Parameters);

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
            MachineName = string.Empty
        };
}
