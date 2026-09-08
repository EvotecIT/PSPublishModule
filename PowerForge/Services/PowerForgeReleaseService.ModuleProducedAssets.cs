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
                                   IsFinalPowerShellProducedAsset(fullPath, plan)
        };
    }

    private static bool IsFinalPowerShellProducedAsset(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        PowerForgeModuleArtefactOutputSummary[] matchingOutputs =
            ResolveMatchingModuleArtefactOutputs(path, plan);
        if (matchingOutputs.Length == 0)
            return IsFinalPowerShellModulePackage(path, plan);

        ArtefactType[] producerTypes = matchingOutputs
            .Select(static output => output.Type)
            .Distinct()
            .ToArray();
        if (producerTypes.Length != 1)
            return false;

        return producerTypes[0] switch
        {
            ArtefactType.Packed => IsModuleArtifactForResolvedVersion(path, plan) &&
                                   IsFinalPowerShellModulePackage(path, plan),
            ArtefactType.Script => IsFinalPowerShellScript(path, plan),
            ArtefactType.ScriptPacked => IsFinalPowerShellScriptPackage(path, plan),
            _ => false
        };
    }

    private static bool IsProducedModuleArtifactForResolvedVersion(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        ArtefactType[] producerTypes = ResolveMatchingModuleArtefactOutputs(path, plan)
            .Select(static output => output.Type)
            .Distinct()
            .ToArray();
        if (producerTypes.Length == 1 &&
            producerTypes[0] is ArtefactType.Script or ArtefactType.ScriptPacked)
        {
            return true;
        }

        return IsModuleArtifactForResolvedVersion(path, plan);
    }

    private static bool IsFinalPowerShellScript(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        PowerForgeModuleArtefactOutputSummary[] matchingOutputs =
            ResolveMatchingModuleArtefactOutputs(path, plan);
        return matchingOutputs.Length > 0 &&
               matchingOutputs.All(static output => output.Type == ArtefactType.Script);
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
            !TryResolveScriptPackedEntryPoint(path, plan, out string? expectedEntryPoint))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 50000)
                return false;

            var archiveEntries = archive.Entries
                .Select(static entry => new
                {
                    Path = entry.FullName.Replace('\\', '/'),
                    IsDirectory = string.IsNullOrWhiteSpace(entry.Name)
                })
                .ToArray();
            var entries = archiveEntries
                .Select(static entry => entry.Path)
                .ToArray();
            var namespaceEntries = archiveEntries
                .Select(static entry => new
                {
                    Path = entry.Path.TrimEnd('/'),
                    entry.IsDirectory
                })
                .ToArray();
            var files = namespaceEntries
                .Where(static entry => !entry.IsDirectory)
                .Select(static entry => entry.Path)
                .ToArray();
            if (files.Length == 0 ||
                files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
                archive.Entries.Any(static entry => HasUnsupportedArchiveEntryType(entry.ExternalAttributes)) ||
                namespaceEntries
                    .GroupBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .Any(static group => group.Count() > 1) ||
                files.Any(file => namespaceEntries.Any(entry =>
                    entry.Path.StartsWith(file + "/", StringComparison.OrdinalIgnoreCase))) ||
                entries.Any(static name => !IsPortableArchiveEntryPath(name)) ||
                files.Any(static name =>
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

            return files.Count(name => string.Equals(
                name,
                expectedEntryPoint,
                StringComparison.Ordinal)) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveScriptPackedEntryPoint(
        string path,
        PowerForgeModuleReleasePlanSummary? plan,
        out string? entryPointRelativePath)
    {
        entryPointRelativePath = null;
        PowerForgeModuleArtefactOutputSummary[] matchingOutputs =
            ResolveMatchingModuleArtefactOutputs(path, plan);
        if (matchingOutputs.Length == 0 ||
            matchingOutputs.Any(static output => output.Type != ArtefactType.ScriptPacked))
        {
            return false;
        }

        string[] entryPoints = matchingOutputs
            .Select(static output => output.EntryPointRelativePath)
            .Where(static entryPoint => !string.IsNullOrWhiteSpace(entryPoint))
            .Select(static entryPoint => entryPoint!.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (entryPoints.Length != 1 ||
            !IsPortableArchiveEntryPath(entryPoints[0]) ||
            !string.Equals(Path.GetExtension(entryPoints[0]), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        entryPointRelativePath = entryPoints[0];
        return true;
    }

    private static bool IsPortableArchivePathRooted(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path))
        {
            return true;
        }

        return path.Length >= 2 &&
               path[1] == ':' &&
               ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));
    }

    private static bool IsPortableArchiveEntryPath(string path)
    {
        if (IsPortableArchivePathRooted(path))
            return false;

        string[] segments = path.Split('/');
        int segmentCount = path.EndsWith("/", StringComparison.Ordinal)
            ? segments.Length - 1
            : segments.Length;
        if (segmentCount == 0)
            return false;

        for (int index = 0; index < segmentCount; index++)
        {
            string segment = segments[index];
            if (segment is "." or ".." || !ArtefactLayoutPathResolver.IsPortableFileName(segment))
                return false;
        }

        return segmentCount == segments.Length || string.IsNullOrEmpty(segments[segments.Length - 1]);
    }

    private static bool HasUnsupportedArchiveEntryType(int externalAttributes)
    {
        int unixFileType = (externalAttributes >> 16) & 0xF000;
        return unixFileType is not 0 and not 0x4000 and not 0x8000;
    }

    private static PowerForgeModuleArtefactOutputSummary[] ResolveMatchingModuleArtefactOutputs(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        string fullPath = Path.GetFullPath(path);
        PowerForgeModuleArtefactOutputSummary[] outputs =
            plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>();
        PowerForgeModuleArtefactOutputSummary[] matchingOutputs = outputs
            .Where(output => output is not null &&
                             !string.IsNullOrWhiteSpace(output.OutputPath) &&
                             MatchesRecordedModuleArtefactOutput(output, fullPath, output.OutputPath!))
            .ToArray();
        if (matchingOutputs.Length == 0)
        {
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            matchingOutputs = outputs
                .Where(output => output is not null &&
                                 string.IsNullOrWhiteSpace(output.OutputPath) &&
                                 !string.IsNullOrWhiteSpace(output.OutputRoot) &&
                                 (output.Type == ArtefactType.Script
                                     ? MatchesRecordedModuleArtefactOutput(output, fullPath, output.OutputRoot)
                                     : string.Equals(
                                         Path.GetFullPath(output.OutputRoot),
                                         directory,
                                         ResolvePathComparison(output.OutputRoot, directory))))
                .ToArray();
        }

        return matchingOutputs;
    }

    private static bool MatchesRecordedModuleArtefactOutput(
        PowerForgeModuleArtefactOutputSummary output,
        string fullCandidatePath,
        string recordedOutputPath)
    {
        string fullRecordedOutputPath = Path.GetFullPath(recordedOutputPath);
        if (string.Equals(
                fullRecordedOutputPath,
                fullCandidatePath,
                ResolvePathComparison(fullRecordedOutputPath, fullCandidatePath)))
        {
            return true;
        }

        if (output.Type != ArtefactType.Script ||
            string.IsNullOrWhiteSpace(output.EntryPointRelativePath))
        {
            return false;
        }

        if (!ArtefactLayoutPathResolver.TryResolveScriptOutputEntryPointPath(
                fullRecordedOutputPath,
                output.EntryPointRelativePath,
                out string? fullEntryPoint))
        {
            return false;
        }

        return string.Equals(
            fullEntryPoint!,
            fullCandidatePath,
            ResolvePathComparison(fullEntryPoint!, fullCandidatePath));
    }

    private static StringComparison ResolvePathComparison(string firstPath, string secondPath)
        => FrameworkCompatibility.GetPathStringComparisonForPath(firstPath) == StringComparison.OrdinalIgnoreCase ||
           FrameworkCompatibility.GetPathStringComparisonForPath(secondPath) == StringComparison.OrdinalIgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

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
