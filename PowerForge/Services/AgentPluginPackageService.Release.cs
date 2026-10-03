using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AgentPluginPackageService
{
    internal static IReadOnlyList<RepositoryTextFileUpdate> PlanCompatibility(
        string sourcePath, string expectedVersion, IReadOnlyDictionary<string, string> plannedContents)
    {
        var normalizedContents = plannedContents.ToDictionary(
            pair => FileSystemPathSafety.ResolveParentDirectoryAliases(pair.Key), pair => pair.Value,
            FrameworkCompatibility.GetPathStringComparison(sourcePath) == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var package = Read(sourcePath, checkCompatibility: false, normalizedContents);
        RequireReleaseVersion(package.Result, expectedVersion);
        var updates = new List<RepositoryTextFileUpdate>();
        foreach (var relativePath in CompatibilityFiles)
        {
            var path = Path.Combine(Path.GetFullPath(sourcePath), relativePath);
            if (!package.Generated.TryGetValue(relativePath, out var generated))
            {
                if (File.Exists(path))
                    throw new InvalidDataException("Obsolete plugin compatibility file: " + relativePath + ". Run agent-plugin sync before releasing.");
                continue;
            }
            if (!File.Exists(path))
                throw new InvalidDataException("Missing plugin compatibility file: " + relativePath + ". Run agent-plugin sync before enabling release synchronization.");
            var original = RepositoryTextFileTransactionService.ReadText(path);
            if (!File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(original)))
                throw new InvalidDataException("Plugin compatibility files must be BOM-free UTF-8. Run agent-plugin sync before releasing: " + relativePath);
            updates.Add(new RepositoryTextFileUpdate(path, original, Encoding.UTF8.GetString(generated)));
        }
        return updates;
    }

    private static JsonDocument ParsePackageJson(string path, IReadOnlyDictionary<string, string>? plannedContents)
    {
        if (plannedContents is null || !plannedContents.TryGetValue(path, out var content))
            return ParseJson(path);
        var document = JsonDocument.Parse(content);
        try { RejectDuplicateKeys(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RequireReleaseVersion(AgentPluginPackageResult result, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || !string.Equals(result.Version, expectedVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin version '{result.Version}' must match release version '{expectedVersion}'.");
    }

    internal static void ValidateReleaseVersion(string sourcePath, string expectedVersion, bool checkCompatibility)
        => RequireReleaseVersion(Read(sourcePath, checkCompatibility).Result, expectedVersion);
}
