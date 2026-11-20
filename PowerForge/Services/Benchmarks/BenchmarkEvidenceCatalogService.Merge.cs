using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    /// <summary>
    /// Consolidates independently produced benchmark evidence bundles into one catalog.
    /// Each source catalog must be accompanied by its normalized result artifacts in the
    /// same directory. Result hashes and catalog-derived lane metadata are verified before
    /// any destination file is replaced.
    /// </summary>
    /// <param name="catalogPath">Destination catalog path.</param>
    /// <param name="sourceCatalogPaths">Source catalog paths to consolidate.</param>
    /// <param name="expectedPlatforms">Optional expected platform set.</param>
    /// <returns>The consolidated catalog.</returns>
    public BenchmarkEvidenceCatalog MergeFiles(
        string catalogPath,
        IEnumerable<string> sourceCatalogPaths,
        IEnumerable<string>? expectedPlatforms = null)
        => MergeFiles(catalogPath, sourceCatalogPaths, expectedPlatforms, null, null, null);

    /// <summary>Merges portable evidence with declared coverage and optional public result URL rebasing.</summary>
    /// <param name="catalogPath">Destination catalog path.</param>
    /// <param name="sourceCatalogPaths">Source catalog paths, including the destination to preserve omitted lanes.</param>
    /// <param name="expectedPlatforms">Optional expected platform set.</param>
    /// <param name="declarations">Authoritative library policy and comparison declarations, or null to merge source declarations.</param>
    /// <param name="resultPathPrefix">Optional root-relative public URL directory. Artifacts are published directly in the output catalog directory.</param>
    /// <param name="policy">Publish policy every lane is re-evaluated against, or null for the default policy.</param>
    /// <returns>The consolidated catalog.</returns>
    public BenchmarkEvidenceCatalog MergeFiles(
        string catalogPath,
        IEnumerable<string> sourceCatalogPaths,
        IEnumerable<string>? expectedPlatforms,
        BenchmarkDeclarationDocument? declarations,
        string? resultPathPrefix = null,
        BenchmarkPublishPolicy? policy = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new ArgumentException("Output catalog path is required.", nameof(catalogPath));
        if (sourceCatalogPaths is null)
            throw new ArgumentNullException(nameof(sourceCatalogPaths));

        string? publicPrefix = NormalizeResultPathPrefix(resultPathPrefix);
        string outputCatalogPath = BenchmarkJson.ResolveWritePath(catalogPath);
        string[] sources = sourceCatalogPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(BenchmarkJson.ResolveWritePath)
            .Distinct(GetPathComparer(outputCatalogPath))
            .ToArray();
        if (sources.Length == 0)
            throw new ArgumentException("At least one source catalog path is required.", nameof(sourceCatalogPaths));

        using var fileLease = BenchmarkFileUpdateLock.Acquire(outputCatalogPath);
        var candidates = new List<BundleLane>();
        var platformSets = new List<string>();
        var comparisons = new List<BenchmarkComparisonDefinition>();
        var libraries = new List<BenchmarkLibraryDeclaration>();
        var superseded = new List<BenchmarkSupersededLane>();
        var sourcePolicies = new List<BenchmarkPublishPolicy>();
        // Every artifact the sources can supply, by hash, so superseded runs travel with the merged bundle.
        var availableArtifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string sourceCatalogPath in sources)
        {
            if (!File.Exists(sourceCatalogPath))
                throw new FileNotFoundException("Benchmark evidence source catalog was not found.", sourceCatalogPath);

            BenchmarkEvidenceCatalog source = ReadCatalog(sourceCatalogPath);
            platformSets.AddRange(source.ExpectedPlatforms);
            comparisons.AddRange(source.Comparisons);
            libraries.AddRange(source.Libraries);
            sourcePolicies.Add(source.Policy ?? BenchmarkPublishPolicy.CreateDefault());
            superseded.AddRange(source.Superseded);
            foreach (BenchmarkSupersededLane history in source.Superseded)
            {
                if (TryReadSupersededArtifact(sourceCatalogPath, history, out byte[]? historyBytes))
                    availableArtifacts[history.ResultSha256] = historyBytes!;
            }
            foreach (BenchmarkEvidenceEntry entry in source.Entries)
            {
                BundleLane lane = LoadBundleLane(sourceCatalogPath, entry);
                // Results recorded in an earlier form are rewritten in the published form, so every merged lane is
                // evaluated and stored the same way and two copies of one run compare equal.
                byte[] published = BenchmarkEvidenceResultCodec.Encode(lane.Result);
                if (!lane.ArtifactBytes.AsSpan().SequenceEqual(published))
                    ReplaceLaneResult(lane, lane.Result);
                candidates.Add(lane);
            }
        }

        var merged = new BenchmarkEvidenceCatalog
        {
            SchemaVersion = CurrentSchemaVersion,
            Policy = policy ?? MergePolicies(sourcePolicies)
        };
        if (declarations is not null)
        {
            ApplyDeclarationDocument(merged, declarations);
        }
        else
        {
            merged.Libraries = MergeLibraries(libraries);
            // Source catalogs may have been edited elsewhere, so their declarations are checked before any lane is
            // evaluated against them.
            merged.Comparisons = comparisons.Select(NormalizeDefinition).ToArray();
        }

        // Libraries the merged declarations do not publish are removed from every candidate before lanes are
        // compared, so a redacted copy and its original select as the same run.
        HashSet<string> unpublished = UnpublishedLibraries(merged);
        foreach (BundleLane candidate in candidates)
        {
            RedactCandidate(candidate, unpublished);
            availableArtifacts[candidate.Entry.ResultSha256] = candidate.ArtifactBytes;
        }
        BundleLane[] selected = SelectDistinctLanes(candidates, superseded);
        foreach (BundleLane lane in selected)
        {
            // Licence markers have no rows in the stored result; they are carried over from the source lane.
            BenchmarkEvidenceCell[] markers = lane.Entry.Cells.Where(IsLicenceMarker).ToArray();
            RejectRowsWithoutLibrary(merged, lane.Entry.Cells, lane.Entry.LaneKey);
            BenchmarkPolicyEvaluation evaluation = Evaluate(
                merged,
                lane.Result,
                lane.Entry.ComparisonId,
                lane.Entry.Framework,
                lane.Entry.Profile,
                lane.Entry.Publish,
                markers);
            if (lane.Entry.Publish && evaluation.LaneIssues.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Published benchmark lane '{lane.Entry.LaneKey}' does not meet the publish policy: " +
                    string.Join(" ", evaluation.LaneIssues));
            }
            RedactMergedLane(lane, evaluation.Cells);
            ApplyEvaluation(lane.Entry, evaluation);
        }

        BenchmarkEvidenceEntry[] entries = selected
            .Select(candidate => candidate.Entry)
            .OrderBy(entry => entry.LaneKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ValidateDistinctPublishedResultPaths(entries);

        string outputDirectory = Path.GetDirectoryName(outputCatalogPath)
                                 ?? throw new InvalidOperationException(
                                     $"Unable to determine the evidence catalog directory for '{outputCatalogPath}'.");
        ValidateDistinctSourceBundleDestinations(outputCatalogPath, selected);
        foreach (BundleLane lane in selected)
        {
            lane.Entry.ResultPath = CreateContentAddressedResultPath(
                publicPrefix is null ? lane.Entry.ResultPath :
                    publicPrefix + "/" + Uri.EscapeDataString(ExtractBundleArtifactFileName(lane.Entry.ResultPath)),
                lane.Entry.ResultSha256);
            lane.Entry.ArtifactRelativePath =
                ExtractBundleArtifactRelativePath(lane.Entry.ResultPath);
            lane.Entry.ArtifactFileName =
                Path.GetFileName(lane.Entry.ArtifactRelativePath);
            string artifactPath = ResolveBundleArtifactPath(
                outputCatalogPath,
                lane.Entry.ArtifactRelativePath,
                lane.Entry.ResultPath);
            lane.DestinationPath = artifactPath;
            lane.Entry.ArtifactDestinationSha256 = ComputeArtifactDestinationSha256(
                outputCatalogPath,
                artifactPath);
        }
        ValidateDistinctBundleDestinations(outputCatalogPath, selected);

        ApplyCompatibility(entries);
        merged.ExpectedPlatforms = NormalizeExpectedPlatforms(expectedPlatforms ?? platformSets);
        merged.Entries = entries;
        // Historical runs follow the same licence policy as current ones: a record lists only libraries the merged
        // declarations publish, and a record that cannot be checked against its file is dropped when it claims one.
        var redactedHistoryRecords = new List<BenchmarkSupersededLane>();
        foreach (BenchmarkSupersededLane history in superseded)
        {
            if (!availableArtifacts.TryGetValue(history.ResultSha256, out byte[]? historyBytes))
            {
                // Without its file the run cannot be redacted; a record still claiming an unpublished library is dropped.
                if (!history.Libraries.Any(unpublished.Contains))
                    redactedHistoryRecords.Add(history);
                continue;
            }
            BenchmarkRunResult historyResult = BenchmarkEvidenceResultCodec.Decode(historyBytes, history.ResultPath);
            BenchmarkRunResult redactedHistory = RedactLibraries(historyResult, unpublished);
            history.Libraries = redactedHistory.Summary.Select(row => row.Library)
                .Concat(redactedHistory.Samples.Select(sample => sample.Library))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            redactedHistoryRecords.Add(history);
        }
        merged.Superseded = NormalizeSuperseded(redactedHistoryRecords, entries);
        // A superseded run stays in the history as a record: when it ran, when it was replaced, its libraries and
        // the SHA-256 of its result. Its result file is not republished with the catalog.
        foreach (BenchmarkSupersededLane history in merged.Superseded)
            history.ArtifactRelativePath = string.Empty;
        ApplyDefinitions(merged);

        Directory.CreateDirectory(outputDirectory);
        foreach (BundleLane lane in selected)
        {
            string destinationPath = lane.DestinationPath!;
            if (File.Exists(destinationPath))
            {
                string existingHash = BenchmarkJson.ComputeFileSha256(destinationPath);
                if (!string.Equals(existingHash, lane.Entry.ResultSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Immutable benchmark artifact '{Path.GetFileName(destinationPath)}' already exists with different content.");
                }
                continue;
            }

            // Immutable content-addressed artifacts are safe to retain when the catalog
            // switch fails. Another catalog in the same bundle may already reference
            // the identical path, so deleting it during rollback can corrupt that
            // independently committed publication.
            BenchmarkJson.WriteBytes(destinationPath, lane.ArtifactBytes);
        }
        BenchmarkJson.Write(outputCatalogPath, merged);
        return merged;
    }

    private static bool TryReadSupersededArtifact(string sourceCatalogPath, BenchmarkSupersededLane history, out byte[]? bytes)
    {
        bytes = null;
        try
        {
            string relative = !string.IsNullOrWhiteSpace(history.ArtifactRelativePath)
                ? history.ArtifactRelativePath
                : ExtractBundleArtifactRelativePath(history.ResultPath);
            string path = ResolveBundleArtifactPath(sourceCatalogPath, relative, history.ResultPath);
            if (!File.Exists(path))
                return false;
            byte[] candidate = File.ReadAllBytes(path);
            if (!string.Equals(ComputeSha256(candidate), history.ResultSha256, StringComparison.OrdinalIgnoreCase))
                return false;
            bytes = candidate;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Declarations given to a merge can restrict a library that was open when the lane was recorded; its rows
    // are then removed from the stored result like they are when a lane is recorded.
    private static void RedactMergedLane(BundleLane lane, BenchmarkEvidenceCell[] cells)
    {
        BenchmarkRunResult redacted = RedactLicenceRestricted(lane.Result, cells);
        if (!ReferenceEquals(redacted, lane.Result))
            ReplaceLaneResult(lane, redacted);
    }

    // Removes unpublished libraries from a candidate before lanes are compared; their cells become cleared markers.
    private static void RedactCandidate(BundleLane lane, ISet<string> unpublished)
    {
        if (unpublished.Count == 0)
            return;
        foreach (BenchmarkEvidenceCell cell in lane.Entry.Cells.Where(cell => unpublished.Contains(cell.Library)))
        {
            cell.ValueMs = null;
            cell.ConfidenceInterval = null;
            cell.SampleCount = 0;
            cell.FailureCount = 0;
            cell.ValidationDetail = string.Empty;
            cell.Outcome = string.Empty;
            cell.Publishable = false;
            if (!IsLicenceMarker(cell))
                cell.Issues = cell.Issues.Append(BenchmarkPublishPolicyEvaluator.NotPublishedLicence + $"library '{cell.Library}' is not published.").ToArray();
        }
        BenchmarkRunResult redacted = RedactLibraries(lane.Result, unpublished);
        if (!ReferenceEquals(redacted, lane.Result))
            ReplaceLaneResult(lane, redacted);
    }

    private static void ReplaceLaneResult(BundleLane lane, BenchmarkRunResult redacted)
    {
        byte[] bytes = BenchmarkEvidenceResultCodec.Encode(redacted);
        string previousHash = lane.Entry.ResultSha256.ToLowerInvariant();
        string hash = ComputeSha256(bytes);
        lane.ArtifactBytes = bytes;
        lane.Result = BenchmarkEvidenceResultCodec.Decode(bytes, lane.Entry.ResultPath);
        lane.Entry.ResultSha256 = hash;
        lane.Entry.Compatibility = BuildCompatibility(redacted);
        lane.Entry.ResultPath = ReplaceContentHash(lane.Entry.ResultPath, previousHash, hash);
    }

    private static BenchmarkPublishPolicy MergePolicies(IReadOnlyCollection<BenchmarkPublishPolicy> policies)
    {
        string[] distinct = policies.Select(value => BenchmarkJson.Serialize(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1)
        {
            throw new InvalidOperationException(
                "Benchmark evidence source catalogs use different publish policies. Pass the policy to merge with explicitly.");
        }
        return policies.FirstOrDefault() ?? BenchmarkPublishPolicy.CreateDefault();
    }

    private static BenchmarkLibraryDeclaration[] MergeLibraries(IEnumerable<BenchmarkLibraryDeclaration> libraries)
    {
        var merged = new Dictionary<string, BenchmarkLibraryDeclaration>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkLibraryDeclaration library in libraries)
        {
            if (merged.TryGetValue(library.Id, out BenchmarkLibraryDeclaration? existing))
            {
                if (BenchmarkJson.ComputeSha256(existing) != BenchmarkJson.ComputeSha256(library))
                    throw new InvalidOperationException($"Conflicting library policy entries for '{library.Id}'. Supply one authoritative declaration document.");
                continue;
            }
            merged.Add(library.Id, library);
        }
        return merged.Values.OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static BundleLane LoadBundleLane(
        string sourceCatalogPath,
        BenchmarkEvidenceEntry sourceEntry)
    {
        ValidateBundleEntry(sourceEntry);
        string artifactPath = ResolveBundleArtifactPath(
            sourceCatalogPath,
            EffectiveBundleArtifactRelativePath(sourceEntry),
            sourceEntry.ResultPath);
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException(
                $"Normalized result artifact for benchmark lane '{sourceEntry.LaneKey}' was not found.",
                artifactPath);
        }

        byte[] bytes = File.ReadAllBytes(artifactPath);
        string fileHash = ComputeSha256(bytes);
        if (!string.Equals(fileHash, sourceEntry.ResultSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Normalized result artifact for benchmark lane '{sourceEntry.LaneKey}' " +
                "does not match the SHA-256 recorded by its source catalog.");
        }

        BenchmarkRunResult result = BenchmarkEvidenceResultCodec.Decode(bytes, artifactPath);
        ValidateBundleEntryMatchesResult(sourceEntry, result);
        string expectedLaneKey = BenchmarkEvidenceIdentity.CreateLaneKey(
            sourceEntry.ComparisonId, sourceEntry.Platform, sourceEntry.Framework, sourceEntry.Profile, sourceEntry.Parameters);
        if (!string.Equals(sourceEntry.LaneKey, expectedLaneKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Benchmark lane key '{sourceEntry.LaneKey}' does not match its dimensions ('{expectedLaneKey}').");
        }
        return new BundleLane(CloneEntry(sourceEntry), bytes, result);
    }

    private static void ValidateBundleEntry(BenchmarkEvidenceEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ComparisonId) ||
            string.IsNullOrWhiteSpace(entry.Platform) ||
            string.IsNullOrWhiteSpace(entry.LaneKey) ||
            string.IsNullOrWhiteSpace(entry.ResultPath) ||
            string.IsNullOrWhiteSpace(entry.Suite))
        {
            throw new InvalidOperationException(
                "Benchmark evidence bundle entries require comparison, platform, lane key, result path, and suite identities.");
        }

        if (entry.ResultSha256.Length != 64 || !entry.ResultSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.LaneKey}' does not contain a valid result SHA-256.");
        }
    }

    private static void ValidateBundleEntryMatchesResult(
        BenchmarkEvidenceEntry entry,
        BenchmarkRunResult result)
    {
        string platform = ResolvePlatform(result, entry.Platform);
        DateTimeOffset generatedUtc = ResolveGeneratedUtc(result);
        Dictionary<string, string> compatibility = BuildCompatibility(result);
        bool mismatch =
            !string.Equals(entry.Suite, result.Suite, StringComparison.Ordinal) ||
            !string.Equals(entry.Platform, platform, StringComparison.OrdinalIgnoreCase) ||
            entry.GeneratedUtc != generatedUtc ||
            !DictionariesMatch(entry.Compatibility, compatibility) ||
            BenchmarkJson.ComputeSha256(entry.Environment) !=
            BenchmarkJson.ComputeSha256(CopyEnvironment(result.Environment));
        if (mismatch)
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.LaneKey}' does not match the normalized result metadata from which it claims to originate.");
        }

        string recordedFramework = BenchmarkEvidenceIdentity.RecordedFramework(result);
        if (BenchmarkEvidenceIdentity.ConflictsWithRecordedFramework(result, entry.Framework))
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.LaneKey}' claims framework '{entry.Framework}' but its result records '{recordedFramework}'.");
        }
        Dictionary<string, string> recordedParameters = ResolveLaneParameters(result, entry.Parameters);
        if (!DictionariesMatch(recordedParameters, entry.Parameters))
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.LaneKey}' has lane parameters that do not match its result's runner block.");
        }

        if (entry.Publish)
        {
            string profile = FormatProfile(entry.Profile);
            ValidateEmbeddedRunModes(result, profile);

            // The source catalog was written only after any imported sidecar was validated.
            // That transient sidecar state is intentionally not serialized into the normalized
            // artifact, so consolidation revalidates every durable publishability invariant.
            ValidatePublishableResult(result, requireValidatedImportProvenance: false);
            ValidateBoundPublishIdentity(result, entry.ComparisonId, profile);
        }
    }

    private static BundleLane[] SelectDistinctLanes(
        IEnumerable<BundleLane> candidates,
        List<BenchmarkSupersededLane> superseded)
    {
        var selected = new List<BundleLane>();
        foreach (IGrouping<string, BundleLane> group in candidates.GroupBy(
                     lane => lane.Entry.LaneKey,
                     StringComparer.OrdinalIgnoreCase))
        {
            BundleLane first = group.OrderByDescending(candidate => candidate.Entry.GeneratedUtc).First();
            if (group.Where(candidate => candidate.Entry.GeneratedUtc == first.Entry.GeneratedUtc)
                .Any(candidate => !EquivalentLane(first.Entry, candidate.Entry)))
            {
                throw new InvalidOperationException(
                    $"Benchmark evidence bundles contain conflicting copies of lane '{first.Entry.LaneKey}'.");
            }

            foreach (BundleLane older in group.Where(candidate =>
                         !string.Equals(candidate.Entry.ResultSha256, first.Entry.ResultSha256, StringComparison.OrdinalIgnoreCase)))
            {
                superseded.Add(CreateSuperseded(older.Entry, first.Entry.GeneratedUtc));
            }

            selected.Add(first);
        }

        return selected.ToArray();
    }

    private static bool EquivalentLane(
        BenchmarkEvidenceEntry left,
        BenchmarkEvidenceEntry right)
        => string.Equals(left.ResultSha256, right.ResultSha256, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.Suite, right.Suite, StringComparison.Ordinal) &&
           left.GeneratedUtc == right.GeneratedUtc &&
           left.Publish == right.Publish &&
           DictionariesMatch(left.Compatibility, right.Compatibility) &&
           BenchmarkJson.ComputeSha256(left.Environment) ==
           BenchmarkJson.ComputeSha256(right.Environment);

    private static string? NormalizeResultPathPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;
        string value = prefix!.Trim().TrimEnd('/');
        if (!value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal) ||
            value.IndexOfAny(new[] { '\\', '?', '#' }) >= 0)
            throw new ArgumentException("Result path prefix must be a root-relative public URL directory without a query or fragment.", nameof(prefix));
        return "/" + NormalizeBundleArtifactRelativePath(value.Substring(1), value).Replace('\\', '/');
    }

    private static bool DictionariesMatch(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count &&
           left.All(item =>
               TryGetValueIgnoreCase(right, item.Key, out string value) &&
               string.Equals(item.Value, value, StringComparison.Ordinal));

    private static string ResolveBundleArtifactPath(
        string catalogPath,
        string artifactRelativePath,
        string resultPath)
    {
        string directory = Path.GetDirectoryName(catalogPath)
                           ?? throw new InvalidOperationException(
                               $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        string resolvedDirectory = BenchmarkJson.ResolveWritePath(directory);
        string normalizedRelativePath =
            NormalizeBundleArtifactRelativePath(artifactRelativePath, resultPath);
        string artifactPath = BenchmarkJson.ResolveWritePath(
            Path.Combine(resolvedDirectory, normalizedRelativePath));
        string relativeToBundle = FrameworkCompatibility.GetRelativePath(
            resolvedDirectory,
            artifactPath);
        if (Path.IsPathRooted(relativeToBundle) ||
            relativeToBundle.Equals("..", StringComparison.Ordinal) ||
            relativeToBundle.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relativeToBundle.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' resolves outside its portable evidence bundle.");
        }

        return artifactPath;
    }

    private static string ExtractBundleArtifactFileName(string resultPath)
    {
        string encodedFileName = ExtractResultPathFileSegment(resultPath);
        string fileName;
        try
        {
            fileName = Uri.UnescapeDataString(encodedFileName);
        }
        catch (UriFormatException exception)
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not contain a valid escaped artifact file name.",
                exception);
        }

        if (!IsPortableBundleFileName(fileName))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact file.");
        }

        return fileName;
    }

    private static string EffectiveBundleArtifactRelativePath(
        BenchmarkEvidenceEntry entry)
        => !string.IsNullOrWhiteSpace(entry.ArtifactRelativePath)
            ? entry.ArtifactRelativePath
            : !string.IsNullOrWhiteSpace(entry.ArtifactFileName)
                ? entry.ArtifactFileName
                : ExtractBundleArtifactFileName(entry.ResultPath);

    private static string CreateContentAddressedResultPath(
        string resultPath,
        string resultSha256)
    {
        string fileName = ExtractBundleArtifactFileName(resultPath);
        string normalizedHash = resultSha256.ToLowerInvariant();
        string decodedStem = Path.GetFileNameWithoutExtension(fileName);
        if (decodedStem.EndsWith(
                "." + normalizedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return resultPath;
        }

        string encodedFileName = ExtractResultPathFileSegment(resultPath);
        string encodedExtension = Path.GetExtension(encodedFileName);
        string encodedStem = Path.GetFileNameWithoutExtension(encodedFileName);
        string contentFileName =
            $"{encodedStem}.{normalizedHash}{encodedExtension}";
        int separatorIndex = Math.Max(
            resultPath.LastIndexOf('/'),
            resultPath.LastIndexOf('\\'));
        string contentResultPath =
            resultPath.Substring(0, separatorIndex + 1) + contentFileName;
        _ = ExtractBundleArtifactFileName(contentResultPath);
        if (contentFileName.Length > 1024)
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' cannot be converted to a portable immutable artifact name.");
        }

        return contentResultPath;
    }

    private static string ExtractResultPathFileSegment(string resultPath)
    {
        string portablePath = resultPath.Replace('\\', '/');
        if (portablePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' must identify a file and cannot end in a directory separator.");
        }
        if (portablePath.IndexOfAny(['?', '#']) >= 0)
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' cannot contain a query or fragment.");
        }

        int separator = portablePath.LastIndexOf('/');
        string fileName = separator >= 0
            ? portablePath.Substring(separator + 1)
            : portablePath;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact file.");
        }

        return fileName;
    }

    private static string ExtractBundleArtifactRelativePath(string resultPath)
    {
        string portablePath = resultPath.Replace('\\', '/').TrimEnd('/');
        if (Uri.TryCreate(portablePath, UriKind.Absolute, out _) ||
            portablePath.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(portablePath))
        {
            return ExtractBundleArtifactFileName(resultPath);
        }

        var segments = new List<string>();
        foreach (string encodedSegment in portablePath.Split('/'))
        {
            if (string.IsNullOrEmpty(encodedSegment) ||
                encodedSegment.Equals(".", StringComparison.Ordinal))
            {
                continue;
            }
            if (encodedSegment.Equals("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{resultPath}' resolves outside its portable evidence bundle.");
            }

            string segment;
            try
            {
                segment = Uri.UnescapeDataString(encodedSegment);
            }
            catch (UriFormatException exception)
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{resultPath}' contains an invalid escaped path segment.",
                    exception);
            }
            if (!IsPortableBundleFileName(segment))
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact path.");
            }
            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact path.");
        }

        return Path.Combine(segments.ToArray());
    }

    private static string NormalizeBundleArtifactRelativePath(
        string artifactRelativePath,
        string resultPath)
    {
        if (string.IsNullOrWhiteSpace(artifactRelativePath) ||
            Path.IsPathRooted(artifactRelativePath))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact path.");
        }

        string[] segments = artifactRelativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                !IsPortableBundleFileName(segment)))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact path.");
        }

        return Path.Combine(segments);
    }

    private static bool IsPortableBundleFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            Encoding.UTF8.GetByteCount(fileName) > 255 ||
            fileName is "." or ".." ||
            fileName.EndsWith(".", StringComparison.Ordinal) ||
            fileName.EndsWith(" ", StringComparison.Ordinal) ||
            fileName.Any(character =>
                character < 32 ||
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'))
        {
            return false;
        }

        string stem = fileName.Split('.')[0];
        return !string.Equals(stem, "CON", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(stem, "PRN", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(stem, "AUX", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(stem, "NUL", StringComparison.OrdinalIgnoreCase) &&
               !IsReservedNumberedDevice(stem, "COM") &&
               !IsReservedNumberedDevice(stem, "LPT");
    }

    private static bool IsReservedNumberedDevice(string value, string prefix)
        => value.Length == 4 &&
           value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
           (value[3] is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3');

    private static void ValidateDistinctBundleDestinations(
        string outputCatalogPath,
        IEnumerable<BundleLane> lanes)
    {
        StringComparer comparer = StringComparer.OrdinalIgnoreCase;
        var owners = new Dictionary<string, BundleLane>(comparer);
        foreach (BundleLane lane in lanes)
        {
            string destinationPath = lane.DestinationPath
                                     ?? throw new InvalidOperationException(
                                         "Benchmark bundle destination was not resolved.");
            if (comparer.Equals(destinationPath, outputCatalogPath))
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{lane.Entry.ResultPath}' would overwrite the destination catalog.");
            }

            if (owners.TryGetValue(destinationPath, out BundleLane? existing))
            {
                throw new InvalidOperationException(
                    $"Benchmark lanes '{existing.Entry.LaneKey}' " +
                    $"and '{lane.Entry.LaneKey}' " +
                    $"would overwrite the same bundle artifact '{Path.GetFileName(destinationPath)}'.");
            }

            owners.Add(destinationPath, lane);
        }
    }

    private static void ValidateDistinctSourceBundleDestinations(
        string outputCatalogPath,
        IEnumerable<BundleLane> lanes)
    {
        string catalogFileName = Path.GetFileName(outputCatalogPath);
        var owners = new Dictionary<string, BundleLane>(StringComparer.OrdinalIgnoreCase);
        foreach (BundleLane lane in lanes)
        {
            string relativePath =
                ExtractBundleArtifactRelativePath(lane.Entry.ResultPath);
            if (string.Equals(relativePath, catalogFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{lane.Entry.ResultPath}' would overwrite the destination catalog.");
            }

            if (owners.TryGetValue(relativePath, out BundleLane? existing))
            {
                throw new InvalidOperationException(
                    $"Benchmark lanes '{existing.Entry.LaneKey}' " +
                    $"and '{lane.Entry.LaneKey}' " +
                    $"would overwrite the same bundle artifact '{relativePath}'.");
            }

            owners.Add(relativePath, lane);
        }
    }

    private static BenchmarkEvidenceEntry CloneEntry(BenchmarkEvidenceEntry entry)
        => BenchmarkJson.ReadBytes<BenchmarkEvidenceEntry>(BenchmarkJson.SerializeCanonicalBytes(entry), "catalog entry");

    private static string ComputeSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static StringComparer GetPathComparer(string path)
        => FrameworkCompatibility.GetPathStringComparison(path) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed class BundleLane
    {
        internal BundleLane(BenchmarkEvidenceEntry entry, byte[] artifactBytes, BenchmarkRunResult result)
        {
            Entry = entry;
            ArtifactBytes = artifactBytes;
            Result = result;
        }

        internal BenchmarkRunResult Result { get; set; }

        internal BenchmarkEvidenceEntry Entry { get; }
        internal byte[] ArtifactBytes { get; set; }
        internal string? DestinationPath { get; set; }
    }

}
