using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static PowerForgeReleaseAssetEntry CreateModuleProducedAssetEntry(
        string fullPath,
        PowerForgeModuleReleasePlanSummary? plan,
        IReadOnlyCollection<string>? producedArtifactPaths)
    {
        if (IsNuGetPackagePath(fullPath))
        {
            bool hasIdentity = TryReadNuGetPackageIdentity(
                fullPath,
                out string? packageId,
                out string? packageVersion);
            string? releaseVersion = ResolveModuleReleaseVersion(plan);
            bool versionMatches = string.IsNullOrWhiteSpace(releaseVersion) ||
                                  string.Equals(packageVersion, releaseVersion, StringComparison.OrdinalIgnoreCase);
            return new PowerForgeReleaseAssetEntry
            {
                Path = fullPath,
                Category = PowerForgeReleaseAssetCategory.Package,
                Source = "ModuleProjectBuild",
                Target = packageId,
                PackageId = packageId,
                Version = packageVersion ?? releaseVersion,
                IsFinalPackageOutput = ContainsProducedModuleArtifact(producedArtifactPaths, fullPath) &&
                                       hasIdentity &&
                                       versionMatches
            };
        }

        return new PowerForgeReleaseAssetEntry
        {
            Path = fullPath,
            Category = PowerForgeReleaseAssetCategory.Module,
            Source = "Module",
            Version = ResolveModuleReleaseVersion(plan),
            IsFinalPackageOutput = ContainsProducedModuleArtifact(producedArtifactPaths, fullPath) &&
                                   (IsFinalPowerShellModulePackage(fullPath, plan) ||
                                    IsFinalPowerShellScriptPackage(fullPath))
        };
    }

    private static bool ContainsProducedModuleArtifact(
        IReadOnlyCollection<string>? producedArtifactPaths,
        string candidatePath)
    {
        if (producedArtifactPaths is null || producedArtifactPaths.Count == 0)
            return false;

        var fullCandidate = Path.GetFullPath(candidatePath);
        return producedArtifactPaths.Any(producedPath =>
        {
            var fullProduced = Path.GetFullPath(producedPath);
            var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(fullProduced) == StringComparison.OrdinalIgnoreCase ||
                             FrameworkCompatibility.GetPathStringComparisonForPath(fullCandidate) == StringComparison.OrdinalIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(fullProduced, fullCandidate, comparison);
        });
    }

    private static bool IsFinalPowerShellScriptPackage(string path)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 50000)
                return false;

            var topLevelScripts = archive.Entries.Count(entry =>
                !string.IsNullOrWhiteSpace(entry.Name) &&
                string.Equals(Path.GetExtension(entry.Name), ".ps1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    entry.FullName.Replace('\\', '/').TrimStart('/'),
                    entry.Name,
                    StringComparison.Ordinal));
            return topLevelScripts == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNuGetPackagePath(string path)
        => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadNuGetPackageIdentity(
        string path,
        out string? packageId,
        out string? packageVersion)
    {
        packageId = null;
        packageVersion = null;
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry? nuspec = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
                return false;

            using Stream stream = nuspec.Open();
            XDocument document = XDocument.Load(stream);
            XElement? metadata = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            packageId = metadata?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?
                .Value
                .Trim();
            packageVersion = metadata?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?
                .Value
                .Trim();
            return !string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(packageVersion);
        }
        catch
        {
            packageId = null;
            packageVersion = null;
            return false;
        }
    }
}
