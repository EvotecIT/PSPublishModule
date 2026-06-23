using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void ValidateReleaseArtefactOutputPathConflicts(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (plan.Artefacts is not { Length: > 0 })
            return;

        var protectedPaths = CollectReleaseProtectedPaths(plan, state);
        if (protectedPaths.Count == 0)
            return;

        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var artefact in plan.Artefacts.Where(static item => item is not null))
        {
            var cfg = artefact.Configuration ?? new ArtefactConfiguration();
            if (cfg.Enabled != true)
                continue;

            var outputRoot = ArtefactLayoutPathResolver.ResolveOutputRoot(
                cfg.Path,
                plan.ProjectRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease,
                artefact.ArtefactType);

            foreach (var protectedPath in protectedPaths)
            {
                if (!IsSameOrChildPath(outputRoot, protectedPath.Path))
                    continue;

                var key = $"{artefact.ArtefactType}|{outputRoot}|{protectedPath.Path}";
                if (!seen.Add(key))
                    continue;

                conflicts.Add(
                    $"Artefact '{artefact.ArtefactType}' output root '{Path.GetFullPath(outputRoot)}' contains {protectedPath.Label} '{Path.GetFullPath(protectedPath.Path)}'. " +
                    $"Use a dedicated artefact path such as '{GetDedicatedArtefactPathHint(artefact.ArtefactType)}' and keep release staging/project build outputs in separate subdirectories.");
            }
        }

        if (conflicts.Count == 0)
            return;

        throw new InvalidOperationException(
            "Release artefact configuration is unsafe:" + Environment.NewLine +
            string.Join(Environment.NewLine, conflicts.Select(static message => "- " + message)));
    }

    private static List<ReleaseProtectedPath> CollectReleaseProtectedPaths(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var paths = new List<ReleaseProtectedPath>();
        var releaseStageRoot = plan.Release?.Configuration is null
            ? null
            : ResolveReleaseStageRoot(plan, plan.Release.Configuration);
        AddReleaseProtectedPath(paths, releaseStageRoot, "release stage root");

        foreach (var result in state.ProjectBuildResults)
        {
            AddReleaseProtectedPath(paths, result.StagingPath, "project build staging path");
            AddReleaseProtectedPath(paths, result.OutputPath, "project build output path");
            AddReleaseProtectedPath(paths, result.ReleaseZipOutputPath, "project build release zip output path");

            foreach (var package in result.Result?.Release?.Projects.SelectMany(static project => project.Packages) ?? Array.Empty<string>())
            {
                AddReleaseProtectedPath(paths, package, "project build package asset");

                var packageDirectory = string.IsNullOrWhiteSpace(package)
                    ? null
                    : Path.GetDirectoryName(Path.GetFullPath(package.Trim().Trim('"')));
                AddReleaseProtectedPath(paths, packageDirectory, "project build package directory");
            }
        }

        return paths;
    }

    private static void AddReleaseProtectedPath(
        List<ReleaseProtectedPath> paths,
        string? path,
        string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        paths.Add(new ReleaseProtectedPath(
            Path.GetFullPath(path!.Trim().Trim('"')),
            label));
    }

    private static string GetDedicatedArtefactPathHint(ArtefactType type)
        => type switch
        {
            ArtefactType.Unpacked => @"Artefacts\Unpacked",
            ArtefactType.Packed => @"Artefacts\Packed",
            _ => $@"Artefacts\{type}"
        };

    private ModuleReleaseCoordinationResult? PrepareUnifiedReleaseAssets(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        string? publishId)
    {
        if (plan.Release?.Configuration is null)
            return null;

        var moduleAssets = CollectModuleReleaseAssets(state.ArtefactResults, publishId);
        var packageAssets = CollectPackageReleaseAssets(state.ProjectBuildResults);
        var allAssets = moduleAssets.Concat(packageAssets).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var stageRoot = ResolveReleaseStageRoot(plan, plan.Release.Configuration);
        string[] finalAssets;
        if (string.IsNullOrWhiteSpace(stageRoot))
        {
            finalAssets = allAssets;
        }
        else
        {
            finalAssets = StageUnifiedReleaseAssets(plan, stageRoot!, moduleAssets, packageAssets);
        }

        return new ModuleReleaseCoordinationResult
        {
            StageRoot = stageRoot ?? string.Empty,
            ModuleAssetPaths = string.IsNullOrWhiteSpace(stageRoot)
                ? moduleAssets
                : finalAssets.Where(path => IsPathBelow(path, Path.Combine(stageRoot!, "modules"))).ToArray(),
            PackageAssetPaths = string.IsNullOrWhiteSpace(stageRoot)
                ? packageAssets
                : finalAssets.Where(path => IsPathBelow(path, Path.Combine(stageRoot!, "nuget"))).ToArray(),
            AssetPaths = finalAssets
        };
    }

    private ModulePublishResult PublishUnifiedGitHubRelease(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (publish is null)
            throw new ArgumentNullException(nameof(publish));
        if (string.IsNullOrWhiteSpace(publish.UserName))
            throw new InvalidOperationException("UserName is required for unified GitHub publishing.");
        var apiKey = ModulePublisher.ResolvePublishApiKey(publish, plan.ProjectRoot);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key (token) is required for unified GitHub publishing.");

        state.ReleaseCoordinationResult = PrepareUnifiedReleaseAssets(plan, state, publish.ID);
        var release = state.ReleaseCoordinationResult
            ?? throw new InvalidOperationException("Release coordination is not configured.");

        if (release.ModuleAssetPaths.Length == 0 && release.PackageAssetPaths.Length == 0)
            throw new InvalidOperationException("No module or package assets were produced for unified GitHub publishing.");

        var owner = publish.UserName!.Trim();
        var repo = string.IsNullOrWhiteSpace(publish.RepositoryName) ? plan.ModuleName : publish.RepositoryName!.Trim();
        var tag = ModulePublisher.GetGitHubTag(publish, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease);
        var versionText = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var isPreRelease = !string.IsNullOrWhiteSpace(plan.PreRelease) && !publish.DoNotMarkAsPreRelease;

        _logger.Info($"Publishing unified GitHub release {owner}/{repo} tag '{tag}' with {release.AssetPaths.Length} asset(s).");
        var gitHub = _gitHubReleasePublisher(new GitHubReleasePublishRequest
        {
            Owner = owner,
            Repository = repo,
            Token = apiKey,
            TagName = tag,
            ReleaseName = tag,
            GenerateReleaseNotes = publish.GenerateReleaseNotes,
            IsDraft = false,
            IsPreRelease = isPreRelease,
            ReuseExistingReleaseOnConflict = true,
            AssetFilePaths = release.AssetPaths
        });

        release.GitHub = gitHub;
        if (!gitHub.Succeeded)
            throw new InvalidOperationException($"Unified GitHub publish failed for tag '{tag}'.");

        var releaseUrl = string.IsNullOrWhiteSpace(gitHub.HtmlUrl)
            ? $"https://github.com/{owner}/{repo}/releases/tag/{tag}"
            : gitHub.HtmlUrl;

        return new ModulePublishResult(
            destination: PublishDestination.GitHub,
            repositoryName: repo,
            userName: owner,
            tagName: tag,
            versionText: versionText,
            isPreRelease: isPreRelease,
            assetPaths: release.AssetPaths,
            releaseUrl: releaseUrl,
            succeeded: true,
            errorMessage: null);
    }

    private static bool ShouldPublishUnifiedGitHubRelease(ModulePipelinePlan plan, PublishConfiguration publish)
        => plan.Release?.Configuration is not null &&
           publish.Destination == PublishDestination.GitHub;

    private static string[] CollectModuleReleaseAssets(IEnumerable<ArtefactBuildResult> artefacts, string? publishId)
    {
        var packed = (artefacts ?? Array.Empty<ArtefactBuildResult>())
            .Where(static artefact => artefact is not null && (artefact.Type == ArtefactType.Packed || artefact.Type == ArtefactType.ScriptPacked))
            .ToArray();

        if (packed.Length == 0)
            return Array.Empty<string>();

        ArtefactBuildResult[] selected;
        if (string.IsNullOrWhiteSpace(publishId))
        {
            selected = new[] { packed[0] };
        }
        else
        {
            var idValue = publishId!.Trim();
            selected = packed.Where(artefact => string.Equals(artefact.Id, idValue, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length == 0)
            {
                var available = packed
                    .Select(static artefact => artefact.Id)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var availableText = available.Length == 0 ? "(none)" : string.Join(", ", available);
                throw new InvalidOperationException($"No packed artefacts matched ID '{publishId}'. Available IDs: {availableText}");
            }
        }

        return selected
            .Select(static artefact => artefact.OutputPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(static path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] CollectPackageReleaseAssets(IEnumerable<ProjectBuildHostExecutionResult> projectBuildResults)
    {
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in projectBuildResults ?? Array.Empty<ProjectBuildHostExecutionResult>())
        {
            foreach (var package in result.Result?.Release?.Projects.SelectMany(static project => project.Packages) ?? Array.Empty<string>())
                TryAddReleasePackageAsset(assets, package);
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

    private static string[] StageUnifiedReleaseAssets(
        ModulePipelinePlan plan,
        string stageRoot,
        IReadOnlyList<string> moduleAssets,
        IReadOnlyList<string> packageAssets)
    {
        var staged = new List<string>();
        var stagedSourcesByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in moduleAssets)
            staged.Add(StageReleaseAsset(stageRoot, "modules", asset, stagedSourcesByPath));
        foreach (var asset in packageAssets)
            staged.Add(StageReleaseAsset(stageRoot, "nuget", asset, stagedSourcesByPath));

        staged.AddRange(WriteReleaseMetadata(plan, stageRoot, staged));
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
                throw new InvalidOperationException(
                    $"Release staging collision: '{existingSource}' and '{sourceFullPath}' both stage to '{targetPath}'. Rename one asset or configure unique output file names.");

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

    private static string[] WriteReleaseMetadata(ModulePipelinePlan plan, string stageRoot, IReadOnlyList<string> stagedAssets)
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

        var manifestPath = Path.Combine(metadataRoot, "release-manifest.json");
        var manifest = new
        {
            moduleName = plan.ModuleName,
            version = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease),
            generatedAtUtc = DateTimeOffset.UtcNow,
            assets = assetEntries
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var checksumsPath = Path.Combine(metadataRoot, "SHA256SUMS.txt");
        File.WriteAllLines(
            checksumsPath,
            assetEntries.Select(static asset => $"{asset.sha256}  {asset.relativePath}"));

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

    private readonly struct ReleaseProtectedPath
    {
        public ReleaseProtectedPath(string path, string label)
        {
            Path = path;
            Label = label;
        }

        public string Path { get; }
        public string Label { get; }
    }
}
