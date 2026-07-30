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
            lane.Entry.ArtifactFileName = ExtractBundleArtifactFileName(lane.Entry.ResultPath);
            string artifactPath = ResolveBundleArtifactPath(
                outputCatalogPath,
                lane.Entry.ArtifactFileName,
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
        string sourceArtifactFileName = string.IsNullOrWhiteSpace(sourceEntry.ArtifactFileName)
            ? ExtractBundleArtifactFileName(sourceEntry.ResultPath)
            : sourceEntry.ArtifactFileName;
        string artifactPath = ResolveBundleArtifactPath(
            sourceCatalogPath,
            sourceArtifactFileName,
            sourceEntry.ResultPath);
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
               EffectiveBundleArtifactFileName(left),
               EffectiveBundleArtifactFileName(right),
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
        string artifactFileName,
        string resultPath)
    {
        if (!IsPortableBundleFileName(artifactFileName))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact file.");
        }

        string directory = Path.GetDirectoryName(catalogPath)
                           ?? throw new InvalidOperationException(
                               $"Unable to determine the evidence catalog directory for '{catalogPath}'.");
        string resolvedDirectory = BenchmarkJson.ResolveWritePath(directory);
        string artifactPath = BenchmarkJson.ResolveWritePath(Path.Combine(resolvedDirectory, artifactFileName));
        string? artifactDirectory = Path.GetDirectoryName(artifactPath);
        if (!string.Equals(
                resolvedDirectory,
                artifactDirectory,
                FrameworkCompatibility.GetPathStringComparison(artifactPath)))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' resolves outside its portable evidence bundle.");
        }

        return artifactPath;
    }

    private static string ExtractBundleArtifactFileName(string resultPath)
    {
        string portablePath = resultPath.Replace('\\', '/').TrimEnd('/');
        if (portablePath.IndexOfAny(['?', '#']) >= 0)
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' cannot contain a query or fragment.");
        }

        string pathComponent = portablePath;
        if (Uri.TryCreate(portablePath, UriKind.Absolute, out Uri? uri))
        {
            pathComponent = uri.IsFile
                ? uri.LocalPath.Replace('\\', '/')
                : uri.AbsolutePath;
        }

        int separator = pathComponent.LastIndexOf('/');
        string fileName = separator >= 0
            ? pathComponent.Substring(separator + 1)
            : pathComponent;
        if (!IsPortableBundleFileName(fileName))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' does not identify a portable bundle artifact file.");
        }

        return fileName;
    }

    private static string EffectiveBundleArtifactFileName(BenchmarkEvidenceEntry entry)
        => string.IsNullOrWhiteSpace(entry.ArtifactFileName)
            ? ExtractBundleArtifactFileName(entry.ResultPath)
            : entry.ArtifactFileName;

    private static string CreateContentAddressedResultPath(
        string resultPath,
        string resultSha256)
    {
        string fileName = ExtractBundleArtifactFileName(resultPath);
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string contentFileName =
            $"{stem}.{resultSha256.ToLowerInvariant()}{extension}";
        if (!IsPortableBundleFileName(contentFileName))
        {
            throw new InvalidOperationException(
                $"Benchmark result path '{resultPath}' cannot be converted to a portable immutable artifact name.");
        }
        int separatorIndex = Math.Max(
            resultPath.LastIndexOf('/'),
            resultPath.LastIndexOf('\\'));
        return resultPath.Substring(0, separatorIndex + 1) + contentFileName;
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
            string fileName = ExtractBundleArtifactFileName(lane.Entry.ResultPath);
            if (string.Equals(fileName, catalogFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Benchmark result path '{lane.Entry.ResultPath}' would overwrite the destination catalog.");
            }

            if (owners.TryGetValue(fileName, out BundleLane? existing))
            {
                throw new InvalidOperationException(
                    $"Benchmark lanes '{existing.Entry.ComparisonId}/{existing.Entry.Platform}/{existing.Entry.RunMode}' " +
                    $"and '{lane.Entry.ComparisonId}/{lane.Entry.Platform}/{lane.Entry.RunMode}' " +
                    $"would overwrite the same bundle artifact '{fileName}'.");
            }

            owners.Add(fileName, lane);
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
