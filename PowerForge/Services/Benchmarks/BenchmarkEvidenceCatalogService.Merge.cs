using System.Security.Cryptography;

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
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new ArgumentException("Output catalog path is required.", nameof(catalogPath));
        if (sourceCatalogPaths is null)
            throw new ArgumentNullException(nameof(sourceCatalogPaths));

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
        foreach (string sourceCatalogPath in sources)
        {
            if (!File.Exists(sourceCatalogPath))
                throw new FileNotFoundException("Benchmark evidence source catalog was not found.", sourceCatalogPath);

            BenchmarkEvidenceCatalog source = BenchmarkJson.Read<BenchmarkEvidenceCatalog>(sourceCatalogPath);
            ValidateCatalogSchema(source);
            if (source.SchemaVersion == 1)
            {
                throw new InvalidOperationException(
                    $"Benchmark evidence source catalog '{sourceCatalogPath}' uses schema 1. " +
                    "Update it with the current PowerForge version before merging so legacy publish flags are demoted and revalidated.");
            }
            platformSets.AddRange(source.ExpectedPlatforms);
            foreach (BenchmarkEvidenceEntry entry in source.Entries)
                candidates.Add(LoadBundleLane(sourceCatalogPath, entry));
        }

        BundleLane[] selected = SelectDistinctLanes(candidates);
        BenchmarkEvidenceEntry[] entries = selected
            .Select(candidate => candidate.Entry)
            .OrderBy(entry => entry.ComparisonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RunMode, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.GeneratedUtc)
            .ToArray();
        ValidateDistinctPublishedResultPaths(entries);

        string outputDirectory = Path.GetDirectoryName(outputCatalogPath)
                                 ?? throw new InvalidOperationException(
                                     $"Unable to determine the evidence catalog directory for '{outputCatalogPath}'.");
        ValidateDistinctSourceBundleDestinations(outputCatalogPath, selected);
        foreach (BundleLane lane in selected)
        {
            lane.Entry.ResultPath = CreateContentAddressedResultPath(
                lane.Entry.ResultPath,
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
        string[] platforms = NormalizeExpectedPlatforms(expectedPlatforms ?? platformSets);
        var merged = new BenchmarkEvidenceCatalog
        {
            SchemaVersion = SupportedSchemaVersion,
            ExpectedPlatforms = platforms,
            Entries = entries,
            Availability = BuildAvailability(entries, platforms)
        };

        var createdArtifacts = new List<string>();
        try
        {
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

                BenchmarkJson.WriteBytes(destinationPath, lane.ArtifactBytes);
                createdArtifacts.Add(destinationPath);
            }
            BenchmarkJson.Write(outputCatalogPath, merged);
            return merged;
        }
        catch (Exception mergeException)
        {
            try
            {
                foreach (string artifactPath in createdArtifacts)
                {
                    if (File.Exists(artifactPath))
                        File.Delete(artifactPath);
                }
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Benchmark evidence merge failed and the previous bundle could not be restored.",
                    new AggregateException(mergeException, rollbackException));
            }

            throw;
        }
    }

    private static BundleLane LoadBundleLane(
        string sourceCatalogPath,
        BenchmarkEvidenceEntry sourceEntry)
    {
        ValidateBundleEntry(sourceEntry);
        bool hasPersistedArtifactPath =
            !string.IsNullOrWhiteSpace(sourceEntry.ArtifactRelativePath) ||
            !string.IsNullOrWhiteSpace(sourceEntry.ArtifactFileName);
        string sourceArtifactRelativePath =
            !string.IsNullOrWhiteSpace(sourceEntry.ArtifactRelativePath)
                ? sourceEntry.ArtifactRelativePath
                : !string.IsNullOrWhiteSpace(sourceEntry.ArtifactFileName)
                    ? sourceEntry.ArtifactFileName
                    : ExtractBundleArtifactFileName(sourceEntry.ResultPath);
        string artifactPath = ResolveBundleArtifactPath(
            sourceCatalogPath,
            sourceArtifactRelativePath,
            sourceEntry.ResultPath);
        if (!File.Exists(artifactPath) &&
            !hasPersistedArtifactPath &&
            TryFindLegacySchemaThreeArtifact(
                sourceCatalogPath,
                sourceEntry,
                out string? discoveredArtifactPath))
        {
            artifactPath = discoveredArtifactPath!;
        }
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException(
                $"Normalized result artifact for benchmark lane " +
                $"'{sourceEntry.ComparisonId}/{sourceEntry.Platform}/{sourceEntry.RunMode}' was not found.",
                artifactPath);
        }

        byte[] bytes = File.ReadAllBytes(artifactPath);
        string fileHash = ComputeSha256(bytes);
        if (!string.Equals(fileHash, sourceEntry.ResultSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Normalized result artifact for benchmark lane " +
                $"'{sourceEntry.ComparisonId}/{sourceEntry.Platform}/{sourceEntry.RunMode}' " +
                "does not match the SHA-256 recorded by its source catalog.");
        }

        BenchmarkRunResult result = BenchmarkJson.ReadBytes<BenchmarkRunResult>(
            bytes,
            artifactPath);
        ValidateBundleEntryMatchesResult(sourceEntry, result);
        return new BundleLane(CloneEntry(sourceEntry), bytes);
    }

    private static void ValidateBundleEntry(BenchmarkEvidenceEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ComparisonId) ||
            string.IsNullOrWhiteSpace(entry.Platform) ||
            string.IsNullOrWhiteSpace(entry.RunMode) ||
            string.IsNullOrWhiteSpace(entry.ResultPath) ||
            string.IsNullOrWhiteSpace(entry.Suite))
        {
            throw new InvalidOperationException(
                "Benchmark evidence bundle entries require comparison, platform, run mode, result path, and suite identities.");
        }

        if (entry.ResultSha256.Length != 64 || !entry.ResultSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.ComparisonId}/{entry.Platform}/{entry.RunMode}' " +
                "does not contain a valid result SHA-256.");
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
            BenchmarkJson.ComputeSha256(result.Environment);
        if (mismatch)
        {
            throw new InvalidOperationException(
                $"Benchmark lane '{entry.ComparisonId}/{entry.Platform}/{entry.RunMode}' " +
                "does not match the normalized result metadata from which it claims to originate.");
        }

        string runMode = entry.RunMode.Trim().ToLowerInvariant();
        ValidateEmbeddedRunModes(result, runMode);
        if (entry.Publish)
        {
            if (!string.Equals(runMode, "full", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Published benchmark lane '{entry.ComparisonId}/{entry.Platform}/{entry.RunMode}' " +
                    "must use Full run mode.");
            }

            // The source catalog was written only after any imported sidecar was validated.
            // That transient sidecar state is intentionally not serialized into the normalized
            // artifact, so consolidation revalidates every durable publishability invariant.
            ValidatePublishableResult(result, requireValidatedImportProvenance: false);
        }
    }

    private static BundleLane[] SelectDistinctLanes(IEnumerable<BundleLane> candidates)
    {
        var selected = new List<BundleLane>();
        foreach (IGrouping<string, BundleLane> group in candidates.GroupBy(
                     lane => string.Join(
                         "\u001f",
                         lane.Entry.ComparisonId,
                         lane.Entry.Platform,
                         lane.Entry.RunMode),
                     StringComparer.OrdinalIgnoreCase))
        {
            BundleLane first = group.First();
            if (group.Skip(1).Any(candidate => !EquivalentLane(first.Entry, candidate.Entry)))
            {
                throw new InvalidOperationException(
                    $"Benchmark evidence bundles contain conflicting copies of lane " +
                    $"'{first.Entry.ComparisonId}/{first.Entry.Platform}/{first.Entry.RunMode}'.");
            }

            selected.Add(first);
        }

        return selected.ToArray();
    }

    private static bool EquivalentLane(
        BenchmarkEvidenceEntry left,
        BenchmarkEvidenceEntry right)
        => string.Equals(left.ResultSha256, right.ResultSha256, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.ResultPath, right.ResultPath, StringComparison.Ordinal) &&
           string.Equals(
               EffectiveBundleArtifactRelativePath(left),
               EffectiveBundleArtifactRelativePath(right),
               StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.Suite, right.Suite, StringComparison.Ordinal) &&
           left.GeneratedUtc == right.GeneratedUtc &&
           left.Publish == right.Publish &&
           DictionariesMatch(left.Compatibility, right.Compatibility) &&
           BenchmarkJson.ComputeSha256(left.Environment) ==
           BenchmarkJson.ComputeSha256(right.Environment);

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
        string portablePath = resultPath.Replace('\\', '/').TrimEnd('/');
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

    private static bool TryFindLegacySchemaThreeArtifact(
        string sourceCatalogPath,
        BenchmarkEvidenceEntry sourceEntry,
        out string? artifactPath)
    {
        artifactPath = null;
        string directory = Path.GetDirectoryName(sourceCatalogPath)
                           ?? throw new InvalidOperationException(
                               $"Unable to determine the evidence catalog directory for '{sourceCatalogPath}'.");
        string fullCatalogPath = BenchmarkJson.ResolveWritePath(sourceCatalogPath);
        StringComparison pathComparison =
            FrameworkCompatibility.GetPathStringComparison(fullCatalogPath);
        foreach (string candidate in Directory
                     .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string fullCandidate = BenchmarkJson.ResolveWritePath(candidate);
            if (string.Equals(fullCandidate, fullCatalogPath, pathComparison))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(sourceEntry.ArtifactDestinationSha256) &&
                !string.Equals(
                    ComputeArtifactDestinationSha256(
                        sourceCatalogPath,
                        fullCandidate),
                    sourceEntry.ArtifactDestinationSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(
                    BenchmarkJson.ComputeFileSha256(fullCandidate),
                    sourceEntry.ResultSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                artifactPath = fullCandidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsPortableBundleFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
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
           value[3] is >= '1' and <= '9';

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
                    $"Benchmark lanes '{existing.Entry.ComparisonId}/{existing.Entry.Platform}/{existing.Entry.RunMode}' " +
                    $"and '{lane.Entry.ComparisonId}/{lane.Entry.Platform}/{lane.Entry.RunMode}' " +
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
                    $"Benchmark lanes '{existing.Entry.ComparisonId}/{existing.Entry.Platform}/{existing.Entry.RunMode}' " +
                    $"and '{lane.Entry.ComparisonId}/{lane.Entry.Platform}/{lane.Entry.RunMode}' " +
                    $"would overwrite the same bundle artifact '{relativePath}'.");
            }

            owners.Add(relativePath, lane);
        }
    }

    private static BenchmarkEvidenceEntry CloneEntry(BenchmarkEvidenceEntry entry)
        => new()
        {
            ComparisonId = entry.ComparisonId,
            Platform = entry.Platform,
            RunMode = entry.RunMode,
            GeneratedUtc = entry.GeneratedUtc,
            Publish = entry.Publish,
            ResultPath = entry.ResultPath,
            ResultSha256 = entry.ResultSha256,
            ArtifactDestinationSha256 = entry.ArtifactDestinationSha256,
            ArtifactFileName = entry.ArtifactFileName,
            ArtifactRelativePath = entry.ArtifactRelativePath,
            Suite = entry.Suite,
            Environment = CopyEnvironment(entry.Environment),
            Compatibility = new Dictionary<string, string>(
                entry.Compatibility,
                StringComparer.OrdinalIgnoreCase),
            Comparable = entry.Comparable,
            CompatibilityIssues = entry.CompatibilityIssues.ToArray()
        };

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
        internal BundleLane(BenchmarkEvidenceEntry entry, byte[] artifactBytes)
        {
            Entry = entry;
            ArtifactBytes = artifactBytes;
        }

        internal BenchmarkEvidenceEntry Entry { get; }
        internal byte[] ArtifactBytes { get; }
        internal string? DestinationPath { get; set; }
    }

}
