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
                                     IsFinalPowerShellScriptPackage(fullPath, plan))
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

    private static bool IsFinalPowerShellScriptPackage(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
            !IsProducedByScriptPackedArtefact(path, plan))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 50000)
                return false;

            var files = archive.Entries
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(static entry => entry.FullName.Replace('\\', '/'))
                .ToArray();
            if (files.Length == 0 ||
                files.Any(static name =>
                    name.StartsWith("/", StringComparison.Ordinal) ||
                    Path.IsPathRooted(name) ||
                    name.Split('/').Any(static segment => segment is "." or "..") ||
                    name.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.github/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return files.Any(static name =>
                string.Equals(Path.GetExtension(name), ".ps1", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProducedByScriptPackedArtefact(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        string fullPath = Path.GetFullPath(path);
        PowerForgeModuleArtefactOutputSummary[] outputs =
            plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>();
        ArtefactType[] exactTypes = outputs
            .Where(output => output is not null &&
                             !string.IsNullOrWhiteSpace(output.OutputPath) &&
                             string.Equals(
                                 Path.GetFullPath(output.OutputPath!),
                                 fullPath,
                                 FrameworkCompatibility.GetPathStringComparison(output.OutputPath!)))
            .Select(static output => output.Type)
            .Distinct()
            .ToArray();
        if (exactTypes.Length > 0)
            return exactTypes.Length == 1 && exactTypes[0] == ArtefactType.ScriptPacked;

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        ArtefactType[] matchingTypes = outputs
            .Where(output => output is not null &&
                             string.IsNullOrWhiteSpace(output.OutputPath) &&
                             !string.IsNullOrWhiteSpace(output.OutputRoot) &&
                             string.Equals(
                                 Path.GetFullPath(output.OutputRoot),
                                 directory,
                                 FrameworkCompatibility.GetPathStringComparison(output.OutputRoot)))
            .Select(static output => output.Type)
            .Distinct()
            .ToArray();
        return matchingTypes.Length == 1 && matchingTypes[0] == ArtefactType.ScriptPacked;
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
