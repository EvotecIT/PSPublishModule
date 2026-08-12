namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static IReadOnlyDictionary<string, string> CaptureModuleArtifactBaseline(
        IEnumerable<string>? configuredPaths)
        => EnumerateModuleArtifactFiles(configuredPaths)
            .ToDictionary(
                static path => path,
                ComputeModuleArtifactSha256,
                StringComparer.OrdinalIgnoreCase);

    internal static string[] ResolveProducedModuleArtifacts(
        IEnumerable<string>? configuredPaths,
        IReadOnlyDictionary<string, string>? baseline)
    {
        var prior = baseline ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return EnumerateModuleArtifactFiles(configuredPaths)
            .Where(path => !prior.TryGetValue(path, out var previousSha256) ||
                           !string.Equals(previousSha256, ComputeModuleArtifactSha256(path), StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateModuleArtifactFiles(IEnumerable<string>? configuredPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuredPath in (configuredPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            foreach (var candidate in PathTokenCandidateResolver.ResolveExistingPaths(configuredPath))
            {
                if (File.Exists(candidate))
                {
                    var fullPath = Path.GetFullPath(candidate);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                    continue;
                }

                if (!Directory.Exists(candidate))
                    continue;

                foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.TopDirectoryOnly))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                }
            }
        }
    }

    private static string ComputeModuleArtifactSha256(string path)
        => ComputeSha256(path);
}
