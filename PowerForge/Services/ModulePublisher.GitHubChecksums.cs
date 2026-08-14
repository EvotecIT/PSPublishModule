using System.Text;

namespace PowerForge;

public sealed partial class ModulePublisher
{
    internal static string WriteDirectGitHubChecksumCatalog(
        IReadOnlyList<ArtefactBuildResult> selected,
        IReadOnlyList<string> assets)
    {
        ArtefactBuildResult primary = (selected ?? Array.Empty<ArtefactBuildResult>()).FirstOrDefault()
            ?? throw new InvalidOperationException("A packed artefact is required to create the GitHub checksum catalog.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(primary.OutputPath))
            ?? throw new InvalidOperationException("The packed artefact output directory could not be resolved.");
        string catalogName = Path.GetFileNameWithoutExtension(primary.OutputPath) + ".SHA256SUMS.txt";
        string catalogPath = Path.Combine(directory, catalogName);
        return WriteGitHubChecksumCatalog(catalogPath, assets);
    }

    internal static string WriteGitHubChecksumCatalog(
        string catalogPath,
        IReadOnlyList<string> assets)
    {
        string resolvedCatalogPath = Path.GetFullPath(catalogPath);
        string? directory = Path.GetDirectoryName(resolvedCatalogPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The GitHub checksum catalog directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var assetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (string asset in (assets ?? Array.Empty<string>()).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(asset);
            if (!assetNames.Add(name))
                throw new InvalidOperationException($"GitHub release assets contain duplicate file name '{name}'.");
            lines.Add($"{DotNetPublishReleaseArtifactVerifier.ComputeSha256(asset)} *{name}");
        }
        if (assetNames.Contains(Path.GetFileName(resolvedCatalogPath)))
            throw new InvalidOperationException("The GitHub checksum catalog file name collides with another release asset.");
        File.WriteAllLines(resolvedCatalogPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return resolvedCatalogPath;
    }
}
