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
    /// Creates final release entries for a .NET publish result, including native installer outputs.
    /// </summary>
    internal static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetArtefactEntries(
        DotNetPublishArtefactResult artifact,
        DotNetPublishPlan? dotNetPlan,
        string? sharedReleaseVersion)
    {
        string?[] artifactPaths = artifact.Category == DotNetPublishArtefactCategory.Installer
            ? (artifact.OutputFiles ?? Array.Empty<string>()).Cast<string?>().ToArray()
            : new[]
            {
                !string.IsNullOrWhiteSpace(artifact.ZipPath) && File.Exists(artifact.ZipPath)
                    ? artifact.ZipPath
                    : artifact.ExePath
            };
        string[] existingPaths = artifactPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .ToArray();
        if (existingPaths.Length == 0)
            yield break;

        var version = ResolveDotNetArtefactVersion(artifact, dotNetPlan, sharedReleaseVersion);

        foreach (string artifactPath in existingPaths)
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = artifactPath,
                Category = artifact.Category switch
                {
                    DotNetPublishArtefactCategory.Bundle => PowerForgeReleaseAssetCategory.Portable,
                    DotNetPublishArtefactCategory.Installer => PowerForgeReleaseAssetCategory.Installer,
                    _ => PowerForgeReleaseAssetCategory.Tool
                },
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
        var version = ResolveDotNetCombinationVersion(
            storePackage.Target,
            storePackage.Framework,
            storePackage.Runtime,
            storePackage.Style,
            dotNetPlan,
            sharedReleaseVersion);
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
