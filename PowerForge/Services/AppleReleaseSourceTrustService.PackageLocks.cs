using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    /// <summary>
    /// Reads the exact remote package map only after every effective lock is proven to match the current Git commit.
    /// </summary>
    internal IReadOnlyDictionary<string, string> ReadApprovedTrackedPackageRevisions(
        string repositoryRoot,
        IEnumerable<string> lockPaths)
    {
        var locks = lockPaths.Select(Path.GetFullPath).Distinct(GetPathComparer()).Where(File.Exists).ToArray();
        foreach (var path in locks)
            EnsureTrackedFile(repositoryRoot, path, "Swift package resolution lock consumed by xcodebuild");
        return ReadApprovedPackageRevisions(locks);
    }

    /// <summary>
    /// Parses a normalized remote URL to exact-revision map from supported Package.resolved schemas.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ReadApprovedPackageRevisions(
        IEnumerable<string> lockPaths)
    {
        var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in lockPaths.Select(Path.GetFullPath).Distinct(GetPathComparer()).Where(File.Exists))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            JsonElement pins;
            if (!(root.TryGetProperty("pins", out pins) ||
                  (root.TryGetProperty("object", out var legacyObject) &&
                   legacyObject.TryGetProperty("pins", out pins))) ||
                pins.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var pin in pins.EnumerateArray())
            {
                var location = ReadJsonString(pin, "location") ?? ReadJsonString(pin, "repositoryURL");
                if (string.IsNullOrWhiteSpace(location) || !pin.TryGetProperty("state", out var state))
                    continue;
                var revision = ReadJsonString(state, "revision");
                if (string.IsNullOrWhiteSpace(revision) ||
                    !Regex.IsMatch(revision, "^(?:[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})$", RegexOptions.CultureInvariant))
                {
                    throw new InvalidOperationException(
                        $"Swift package '{location}' in '{path}' is not bound to an exact Git revision.");
                }

                var normalized = NormalizePackageLocation(location!);
                if (approved.TryGetValue(normalized, out var existing) &&
                    !existing.Equals(revision, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Swift package '{location}' resolves to conflicting exact revisions across the approved Package.resolved graph.");
                }
                approved[normalized] = revision!.ToLowerInvariant();
            }
        }

        return approved;
    }

    private static string[] FindTrackedPackageLocks(
        IReadOnlyCollection<string> effectiveLockPaths,
        string dependencyIdentity)
    {
        return effectiveLockPaths
            .Select(Path.GetFullPath)
            .Distinct(GetPathComparer())
            .Where(File.Exists)
            .Where(path => PackageLockBindsDependency(path, dependencyIdentity))
            .ToArray();
    }

    private static bool PackageLockBindsDependency(string path, string dependencyIdentity)
        => ReadPackagePinRevisions(path, dependencyIdentity).Length > 0;

    private static string[] ReadPackagePinRevisions(string path, string dependencyIdentity)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Swift package resolution lock is not valid JSON: {path}", exception);
        }

        var revisions = new List<string>();
        using (document)
        {
            var root = document.RootElement;
            JsonElement pins;
            if (root.TryGetProperty("pins", out pins) ||
                (root.TryGetProperty("object", out var legacyObject) &&
                 legacyObject.TryGetProperty("pins", out pins)))
            {
                if (pins.ValueKind != JsonValueKind.Array)
                    return Array.Empty<string>();
                foreach (var pin in pins.EnumerateArray())
                {
                    if (!PackagePinMatchesIdentity(pin, dependencyIdentity) ||
                        !pin.TryGetProperty("state", out var state))
                    {
                        continue;
                    }

                    var revision = ReadJsonString(state, "revision");
                    if (!string.IsNullOrWhiteSpace(revision) &&
                        (revision!.Length == 40 || revision.Length == 64) &&
                        revision.All(Uri.IsHexDigit))
                    {
                        revisions.Add(revision.ToLowerInvariant());
                    }

                    if (!LooksLikeRepositoryLocation(dependencyIdentity) &&
                        !string.IsNullOrWhiteSpace(ReadJsonString(state, "version")))
                    {
                        throw new InvalidOperationException(
                            $"Swift registry package '{dependencyIdentity}' is version-bound but cannot be source-inspected as a Git revision.");
                    }
                }
            }
        }

        return revisions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolvePackageRevision(
        IReadOnlyCollection<string> effectiveLockPaths,
        string dependencyIdentity,
        string? exactRevision = null)
    {
        if (!string.IsNullOrWhiteSpace(exactRevision))
            return exactRevision!.ToLowerInvariant();
        var revisions = effectiveLockPaths
            .Where(File.Exists)
            .SelectMany(path => ReadPackagePinRevisions(path, dependencyIdentity))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (revisions.Length != 1)
        {
            throw new InvalidOperationException(
                $"Remote Swift package '{dependencyIdentity}' must resolve to one exact Git revision across the effective Package.resolved graph.");
        }
        return revisions[0];
    }

    private static bool PackagePinMatchesIdentity(JsonElement pin, string dependencyIdentity)
    {
        var location = ReadJsonString(pin, "location") ?? ReadJsonString(pin, "repositoryURL");
        if (LooksLikeRepositoryLocation(dependencyIdentity))
        {
            return !string.IsNullOrWhiteSpace(location) &&
                   NormalizePackageLocation(location!).Equals(
                       NormalizePackageLocation(dependencyIdentity),
                       StringComparison.OrdinalIgnoreCase);
        }

        var identity = ReadJsonString(pin, "identity") ?? ReadJsonString(pin, "package") ?? location;
        return !string.IsNullOrWhiteSpace(identity) &&
               identity!.Trim().Equals(dependencyIdentity.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool LooksLikeRepositoryLocation(string value)
        => value.Contains("://", StringComparison.Ordinal) ||
           value.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
           value.IndexOf('/') >= 0 ||
           value.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePackageLocation(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        return normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - 4)
            : normalized;
    }

    private static string[] ResolveEffectivePackageLockPaths(
        string projectMetadataPath,
        IReadOnlyCollection<string> metadataPaths)
    {
        var projectContainer = Path.GetDirectoryName(projectMetadataPath)!;
        var paths = new HashSet<string>(GetPathComparer())
        {
            Path.Combine(projectContainer, "project.xcworkspace", "xcshareddata", "swiftpm", "Package.resolved")
        };
        var knownMetadata = new HashSet<string>(metadataPaths.Select(Path.GetFullPath), GetPathComparer());
        foreach (var workspaceMetadata in knownMetadata.Where(path =>
                     path.EndsWith("contents.xcworkspacedata", StringComparison.OrdinalIgnoreCase) &&
                     File.Exists(path)))
        {
            if (WorkspaceReferencesContainer(
                    workspaceMetadata,
                    projectContainer,
                    knownMetadata,
                    new HashSet<string>(GetPathComparer())))
            {
                paths.Add(Path.Combine(
                    Path.GetDirectoryName(workspaceMetadata)!,
                    "xcshareddata",
                    "swiftpm",
                    "Package.resolved"));
            }
        }

        return paths.ToArray();
    }

    private static bool WorkspaceReferencesContainer(
        string workspaceMetadata,
        string targetContainer,
        ISet<string> knownMetadata,
        ISet<string> visited)
    {
        var normalizedMetadata = Path.GetFullPath(workspaceMetadata);
        if (!visited.Add(normalizedMetadata))
            return false;

        var workspaceContainer = Path.GetDirectoryName(normalizedMetadata)!;
        var workspaceRoot = Path.GetDirectoryName(workspaceContainer)!;
        var document = XDocument.Load(normalizedMetadata, LoadOptions.None);
        foreach (var candidate in EnumerateWorkspaceReferences(document.Root, workspaceRoot, workspaceRoot))
        {
            var normalizedCandidate = Path.GetFullPath(candidate);
            if (GetPathComparer().Equals(normalizedCandidate, Path.GetFullPath(targetContainer)))
                return true;
            if (!normalizedCandidate.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
                continue;

            var nestedMetadata = Path.Combine(normalizedCandidate, "contents.xcworkspacedata");
            if (knownMetadata.Contains(nestedMetadata) &&
                WorkspaceReferencesContainer(nestedMetadata, targetContainer, knownMetadata, visited))
            {
                return true;
            }
        }

        return false;
    }
}
