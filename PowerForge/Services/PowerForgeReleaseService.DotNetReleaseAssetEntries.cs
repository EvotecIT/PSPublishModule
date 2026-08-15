namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateLegacyToolAssetEntries(
        PowerForgeToolReleaseArtifactResult artifact)
    {
        var paths = !string.IsNullOrWhiteSpace(artifact.ZipPath) && File.Exists(artifact.ZipPath)
            ? new[] { artifact.ZipPath }
            : new[] { artifact.ExecutablePath, artifact.CommandAliasPath };

        foreach (var path in paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Tool,
                Source = "LegacyTools",
                Target = artifact.Target,
                Version = artifact.Version,
                Runtime = artifact.Runtime,
                Framework = artifact.Framework,
                Style = artifact.Flavor.ToString(),
                IsFinalPackageOutput = true
            };
        }
    }

    /// <summary>
    /// Creates the final release entry for a .NET publish result, preferring its archive and otherwise retaining its executable.
    /// </summary>
    internal static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetArtefactEntries(
        DotNetPublishArtefactResult artifact,
        DotNetPublishPlan? dotNetPlan,
        string? sharedReleaseVersion)
    {
        var artifactPath = !string.IsNullOrWhiteSpace(artifact.ZipPath) && File.Exists(artifact.ZipPath)
            ? artifact.ZipPath
            : !string.IsNullOrWhiteSpace(artifact.ExePath) && File.Exists(artifact.ExePath)
                ? artifact.ExePath
                : null;
        if (string.IsNullOrWhiteSpace(artifactPath))
            yield break;

        var version = ResolveDotNetArtefactVersion(artifact, dotNetPlan, sharedReleaseVersion);

        yield return new PowerForgeReleaseAssetEntry
        {
            Path = artifactPath!,
            Category = artifact.Category == DotNetPublishArtefactCategory.Bundle
                ? PowerForgeReleaseAssetCategory.Portable
                : PowerForgeReleaseAssetCategory.Tool,
            Source = "DotNetPublish",
            Target = artifact.Target,
            Version = version,
            Runtime = artifact.Runtime,
            Framework = artifact.Framework,
            Style = artifact.Style.ToString(),
            BundleId = artifact.BundleId,
            IsFinalPackageOutput = true
        };

        foreach (string evidencePath in (artifact.EvidencePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = Path.GetFullPath(evidencePath),
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "DotNetPublish",
                Target = artifact.Target,
                Version = version,
                Runtime = artifact.Runtime,
                Framework = artifact.Framework,
                Style = artifact.Style.ToString(),
                BundleId = artifact.BundleId,
                IsFinalPackageOutput = true
            };
        }
    }

    /// <summary>
    /// Creates final Store/MSIX release entries with their target-specific release version.
    /// </summary>
    internal static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetStorePackageEntries(
        DotNetPublishStorePackageResult storePackage,
        DotNetPublishPlan? dotNetPlan,
        string? sharedReleaseVersion)
    {
        var version = ResolveDotNetTargetVersion(storePackage.Target, dotNetPlan, sharedReleaseVersion);
        foreach (var path in (storePackage.OutputFiles ?? Array.Empty<string>())
            .Concat(storePackage.UploadFiles ?? Array.Empty<string>())
            .Concat(storePackage.SymbolFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Store,
                Source = "DotNetPublish",
                Target = storePackage.Target,
                Version = version,
                Runtime = storePackage.Runtime,
                Framework = storePackage.Framework,
                Style = storePackage.Style.ToString(),
                IsFinalPackageOutput = true
            };
        }
    }
}
