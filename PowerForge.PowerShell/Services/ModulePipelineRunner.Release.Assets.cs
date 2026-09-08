using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static string[] CollectModuleReleaseAssets(
        IEnumerable<ArtefactBuildResult> artefacts,
        string? publishId,
        string scriptArchiveRoot)
    {
        var releaseArtefacts = (artefacts ?? Array.Empty<ArtefactBuildResult>())
            .Where(static artefact => artefact is not null &&
                                      artefact.Type is ArtefactType.Packed or ArtefactType.Script or ArtefactType.ScriptPacked)
            .ToArray();

        if (releaseArtefacts.Length == 0)
            return Array.Empty<string>();

        ArtefactBuildResult[] selected;
        if (string.IsNullOrWhiteSpace(publishId))
        {
            selected = new[] { releaseArtefacts[0] };
        }
        else
        {
            var idValue = publishId!.Trim();
            selected = releaseArtefacts
                .Where(artefact => string.Equals(artefact.Id, idValue, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (selected.Length == 0)
            {
                var available = releaseArtefacts
                    .Select(static artefact => artefact.Id)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var availableText = available.Length == 0 ? "(none)" : string.Join(", ", available);
                throw new InvalidOperationException(
                    $"No release artefacts matched ID '{publishId}'. Available IDs: {availableText}");
            }
        }

        var scriptSourcesByArchive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] selectedOutputPaths = selected
            .SelectMany(static artefact => new[] { artefact.OutputPath }.Concat(artefact.EvidencePaths))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return selected
            .SelectMany(artefact => ResolveModuleReleaseArtefactPaths(
                artefact,
                scriptArchiveRoot,
                scriptSourcesByArchive,
                selectedOutputPaths).Concat(artefact.EvidencePaths))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ResolveModuleReleaseArtefactPaths(
        ArtefactBuildResult artefact,
        string scriptArchiveRoot,
        IDictionary<string, string> scriptSourcesByArchive,
        IReadOnlyList<string> selectedOutputPaths)
    {
        if (artefact.Type != ArtefactType.Script)
            return new[] { artefact.OutputPath };

        if (!ArtefactLayoutPathResolver.TryResolveScriptOutputEntryPointPath(
                artefact.OutputPath,
                artefact.EntryPointRelativePath,
                out string? entryPointPath))
        {
            throw new InvalidOperationException(
                $"Script release artefact '{artefact.Id}' did not report an entry point contained by its output root.");
        }

        var outputRoot = Path.GetFullPath(artefact.OutputPath);
        if (!Directory.Exists(outputRoot))
            throw new DirectoryNotFoundException($"Script release artefact layout was not found: {outputRoot}");

        Directory.CreateDirectory(scriptArchiveRoot);
        var archiveName = Path.GetFileNameWithoutExtension(entryPointPath) + ".zip";
        var archivePath = Path.GetFullPath(Path.Combine(scriptArchiveRoot, archiveName));
        if (IsSameOrChildPath(outputRoot, archivePath))
        {
            throw new InvalidOperationException(
                $"Script release archive '{archivePath}' must be outside its source layout '{outputRoot}'.");
        }
        string? conflictingOutput = selectedOutputPaths.FirstOrDefault(path =>
            PathsEqual(path, archivePath) ||
            (Directory.Exists(path) && IsSameOrChildPath(path, archivePath)));
        if (conflictingOutput is not null)
        {
            throw new InvalidOperationException(
                $"Script release archive '{archivePath}' overlaps selected artefact output or evidence '{conflictingOutput}'. " +
                "Configure unique release asset paths so synthesizing a Script archive cannot replace another payload.");
        }
        if (scriptSourcesByArchive.TryGetValue(archivePath, out var existingSource) &&
            !PathsEqual(existingSource, outputRoot))
        {
            throw new InvalidOperationException(
                $"Script release layouts '{existingSource}' and '{outputRoot}' resolve to the same archive '{archivePath}'. Configure unique script entry-point names.");
        }

        scriptSourcesByArchive[archivePath] = outputRoot;
        ArtefactBuilder.CreateDeterministicZipFromDirectoryContents(
            outputRoot,
            archivePath,
            ArtefactBuilder.ScriptStartsWithShebang(entryPointPath!)
                ? new[] { artefact.EntryPointRelativePath! }
                : Array.Empty<string>());
        return new[] { archivePath };
    }

    private static string[] CollectPackageReleaseAssets(
        IEnumerable<ProjectBuildHostExecutionResult> projectBuildResults)
    {
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in projectBuildResults ?? Array.Empty<ProjectBuildHostExecutionResult>())
        {
            foreach (var package in result.Result?.Release?.Projects.SelectMany(static project =>
                         project.Packages.Concat(project.SymbolPackages)) ?? Array.Empty<string>())
            {
                TryAddReleasePackageAsset(assets, package);
            }
        }

        return assets.ToArray();
    }

    private static void TryAddReleasePackageAsset(HashSet<string> assets, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path!.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            return;

        if (fullPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            assets.Add(fullPath);
        }
    }

    private static string? ResolveReleaseStageRoot(ModulePipelinePlan plan, ReleaseConfiguration release)
    {
        if (string.IsNullOrWhiteSpace(release.StageRoot))
            return null;

        var formatted = ModulePathTokenFormatter.ReplacePathTokens(
            release.StageRoot!,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease);

        return PathValueResolver.Resolve(plan.ProjectRoot, formatted);
    }

    private static string ResolveDefaultReleaseRoot(ModulePipelinePlan plan)
        => Path.Combine(
            plan.ProjectRoot,
            "Artefacts",
            "ReleaseMetadata",
            plan.ModuleName,
            ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease));

    private static string ResolveScriptReleaseArchiveRoot(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        string releaseRoot,
        bool hasConfiguredStageRoot)
    {
        var candidate = Path.Combine(releaseRoot, "modules");
        if (hasConfiguredStageRoot || !state.ArtefactResults.Any(artefact =>
                artefact.Type == ArtefactType.Script &&
                IsSameOrChildPath(artefact.OutputPath, candidate)))
        {
            return candidate;
        }

        return Path.Combine(
            ResolveSynchronizedReleaseStateRoot(plan.ProjectRoot),
            "release-assets",
            plan.ModuleName,
            ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease),
            "modules");
    }

    private static string[] StageUnifiedReleaseAssets(
        ModulePipelinePlan plan,
        string stageRoot,
        IReadOnlyList<string> moduleAssets,
        IReadOnlyList<string> packageAssets,
        string releaseVersion)
    {
        var staged = new List<string>();
        var stagedSourcesByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in moduleAssets)
            staged.Add(StageReleaseAsset(stageRoot, "modules", asset, stagedSourcesByPath));
        foreach (var asset in packageAssets)
            staged.Add(StageReleaseAsset(stageRoot, "nuget", asset, stagedSourcesByPath));

        staged.AddRange(WriteReleaseMetadata(plan, stageRoot, staged, releaseVersion));
        return staged.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string StageReleaseAsset(
        string stageRoot,
        string category,
        string sourcePath,
        Dictionary<string, string> stagedSourcesByPath)
    {
        var targetRoot = Path.Combine(stageRoot, category);
        Directory.CreateDirectory(targetRoot);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var targetPath = Path.GetFullPath(Path.Combine(targetRoot, Path.GetFileName(sourcePath)));
        if (stagedSourcesByPath.TryGetValue(targetPath, out var existingSource))
        {
            if (!PathsEqual(existingSource, sourceFullPath))
            {
                throw new InvalidOperationException(
                    $"Release staging collision: '{existingSource}' and '{sourceFullPath}' both stage to '{targetPath}'. Rename one asset or configure unique output file names.");
            }

            return targetPath;
        }

        stagedSourcesByPath[targetPath] = sourceFullPath;
        if (File.Exists(targetPath) && PathsEqual(targetPath, sourcePath))
            return targetPath;

        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathBelow(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] WriteReleaseMetadata(
        ModulePipelinePlan plan,
        string stageRoot,
        IReadOnlyList<string> stagedAssets,
        string releaseVersion)
    {
        var metadataRoot = Path.Combine(stageRoot, "metadata");
        Directory.CreateDirectory(metadataRoot);

        var assetEntries = stagedAssets
            .Where(static path => File.Exists(path))
            .Select(path => new
            {
                fileName = Path.GetFileName(path),
                relativePath = ToSlashPath(ComputeRelativePath(stageRoot, path)),
                length = new FileInfo(path).Length,
                sha256 = ComputeSha256(path)
            })
            .OrderBy(static asset => asset.relativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateAssetName = assetEntries
            .GroupBy(static asset => asset.fileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAssetName is not null)
        {
            throw new InvalidOperationException(
                $"Release assets contain multiple files named '{duplicateAssetName.Key}'. " +
                "GitHub release assets require unique file names.");
        }

        var manifestPath = Path.Combine(metadataRoot, "release-manifest.json");
        var moduleVersion = ModulePathTokenFormatter.FormatVersionWithPreRelease(
            plan.ResolvedVersion,
            plan.PreRelease);
        var manifest = new
        {
            moduleName = plan.ModuleName,
            version = moduleVersion,
            moduleVersion,
            releaseVersion,
            generatedAtUtc = DateTimeOffset.UtcNow,
            assets = assetEntries
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var checksumsPath = Path.Combine(metadataRoot, "SHA256SUMS.txt");
        File.WriteAllLines(
            checksumsPath,
            assetEntries.Select(static asset => $"{asset.sha256} *{asset.fileName}"));

        return new[] { manifestPath, checksumsPath };
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ToSlashPath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        var baseFull = AppendDirectorySeparatorChar(Path.GetFullPath(baseDir));
        var full = Path.GetFullPath(fullPath);
        var baseUri = new Uri(baseFull);
        var pathUri = new Uri(full);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}
