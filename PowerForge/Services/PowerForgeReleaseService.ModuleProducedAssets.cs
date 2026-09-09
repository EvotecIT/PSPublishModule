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
            ArtefactType.Script => IsFinalPowerShellScript(path, plan) ||
                                   IsFinalPowerShellScriptPackage(path, plan, ArtefactType.Script),
            ArtefactType.ScriptPacked => IsFinalPowerShellScriptPackage(path, plan, ArtefactType.ScriptPacked),
            _ => false
        };
    }

    private static bool IsScriptArtefactSourceReplacedByReleaseArchive(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath = Path.GetFullPath(path);
        return (plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>())
            .Any(output => output is not null &&
                           output.Type == ArtefactType.Script &&
                           !string.IsNullOrWhiteSpace(output.ReleaseAssetPath) &&
                           IsSameOrBelowScriptLayout(fullPath, output));
    }

    private static bool IsSameOrBelowScriptLayout(
        string fullPath,
        PowerForgeModuleArtefactOutputSummary output)
    {
        string root = string.IsNullOrWhiteSpace(output.OutputPath)
            ? output.OutputRoot
            : output.OutputPath!;
        if (string.IsNullOrWhiteSpace(root))
            return false;

        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, ResolvePathComparison(fullPath, fullRoot)) ||
               IsModuleReleaseAssetBelowDirectory(fullPath, fullRoot);
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
        PowerForgeModuleReleasePlanSummary? plan,
        ArtefactType expectedProducerType)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
            !TryResolveScriptPackageEntryPoint(path, plan, expectedProducerType, out string? expectedEntryPoint))
        {
            return false;
        }

        return PowerShellScriptArchiveValidator.TryValidate(
            path,
            expectedEntryPoint,
            out _);
    }

    private static bool TryResolveScriptPackageEntryPoint(
        string path,
        PowerForgeModuleReleasePlanSummary? plan,
        ArtefactType expectedProducerType,
        out string? entryPointRelativePath)
    {
        entryPointRelativePath = null;
        PowerForgeModuleArtefactOutputSummary[] matchingOutputs =
            ResolveMatchingModuleArtefactOutputs(path, plan);
        if (matchingOutputs.Length == 0 ||
            matchingOutputs.Any(output => output.Type != expectedProducerType))
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
            !string.Equals(Path.GetExtension(entryPoints[0]), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        entryPointRelativePath = entryPoints[0];
        return true;
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
                             ((!string.IsNullOrWhiteSpace(output.ReleaseAssetPath) &&
                               string.Equals(
                                   Path.GetFullPath(output.ReleaseAssetPath!),
                                   fullPath,
                                   ResolvePathComparison(output.ReleaseAssetPath!, fullPath))) ||
                              (!string.IsNullOrWhiteSpace(output.OutputPath) &&
                               MatchesRecordedModuleArtefactOutput(output, fullPath, output.OutputPath!))))
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
