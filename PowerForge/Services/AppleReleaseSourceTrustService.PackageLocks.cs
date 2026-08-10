using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
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

        using (document)
        {
            var root = document.RootElement;
            JsonElement pins;
            if (root.TryGetProperty("pins", out pins) ||
                (root.TryGetProperty("object", out var legacyObject) &&
                 legacyObject.TryGetProperty("pins", out pins)))
            {
                if (pins.ValueKind != JsonValueKind.Array)
                    return false;
                foreach (var pin in pins.EnumerateArray())
                {
                    if (!PackagePinMatchesIdentity(pin, dependencyIdentity) ||
                        !pin.TryGetProperty("state", out var state))
                    {
                        continue;
                    }

                    var revision = ReadJsonString(state, "revision");
                    if (!string.IsNullOrWhiteSpace(revision) &&
                        revision!.Length == 40 &&
                        revision.All(Uri.IsHexDigit))
                    {
                        return true;
                    }

                    if (!LooksLikeRepositoryLocation(dependencyIdentity) &&
                        !string.IsNullOrWhiteSpace(ReadJsonString(state, "version")))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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
