using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void ValidateReleaseArtefactOutputPathConflicts(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (plan.Artefacts is not { Length: > 0 })
            return;

        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddArtefactOutputPathConflicts(plan, conflicts);

        var protectedPaths = CollectReleaseProtectedPaths(plan, state);

        if (protectedPaths.Count > 0)
        {
            foreach (var artefact in plan.Artefacts.Where(static item => item is not null))
            {
                var cfg = artefact.Configuration ?? new ArtefactConfiguration();
                if (cfg.Enabled != true)
                    continue;

                foreach (var destructivePath in CollectArtefactDestructivePaths(plan, artefact, cfg))
                {
                    foreach (var protectedPath in protectedPaths)
                    {
                        if (!DoesDestructivePathAffectProtectedPath(destructivePath, protectedPath))
                            continue;

                        var key = $"{artefact.ArtefactType}|{destructivePath.Kind}|{destructivePath.Path}|{protectedPath.Path}";
                        if (!seen.Add(key))
                            continue;

                        conflicts.Add(
                            $"Artefact '{artefact.ArtefactType}' {destructivePath.Label} '{Path.GetFullPath(destructivePath.Path)}' would affect {protectedPath.Label} '{Path.GetFullPath(protectedPath.Path)}'. " +
                            $"Use a dedicated artefact path such as '{GetDedicatedArtefactPathHint(artefact.ArtefactType)}' and keep release staging/project build outputs in separate subdirectories.");
                    }
                }
            }
        }

        if (conflicts.Count == 0)
            return;

        throw new InvalidOperationException(
            "Release artefact configuration is unsafe:" + Environment.NewLine +
            string.Join(Environment.NewLine, conflicts.Select(static message => "- " + message)));
    }

    private static List<ArtefactDestructivePath> CollectArtefactDestructivePaths(
        ModulePipelinePlan plan,
        ConfigurationArtefactSegment artefact,
        ArtefactConfiguration cfg)
    {
        var paths = new List<ArtefactDestructivePath>();
        var outputRoot = ArtefactLayoutPathResolver.ResolveOutputRoot(
            cfg.Path,
            plan.ProjectRoot,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            artefact.ArtefactType);

        if (artefact.ArtefactType == ArtefactType.Unpacked || artefact.ArtefactType == ArtefactType.Script)
        {
            bool scriptArtefact = artefact.ArtefactType == ArtefactType.Script;
            var requiredModulesRoot = ArtefactLayoutPathResolver.ResolveRequiredModulesRootForUnpacked(
                cfg,
                outputRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease);
            var modulesRoot = ArtefactLayoutPathResolver.ResolveModulesRootForUnpacked(
                cfg,
                outputRoot,
                requiredModulesRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease);

            if (cfg.DoNotClear != true)
            {
                paths.Add(new ArtefactDestructivePath(
                    outputRoot,
                    "output root",
                    ArtefactDestructivePathKind.DirectoryTree));
            }

            paths.Add(new ArtefactDestructivePath(
                scriptArtefact ? modulesRoot : Path.Combine(modulesRoot, plan.ModuleName),
                scriptArtefact ? "main script copy root" : "main module copy root",
                ArtefactDestructivePathKind.DirectoryTree));

            if (cfg.RequiredModules.Enabled == true)
            {
                foreach (var requiredModule in FilterRequiredModulesForArtefactValidation(plan.RequiredModulesForPackaging, cfg.RequiredModules.ExcludeModuleName))
                {
                    paths.Add(new ArtefactDestructivePath(
                        Path.Combine(requiredModulesRoot, requiredModule.ModuleName!),
                        "required module copy root",
                        ArtefactDestructivePathKind.DirectoryTree));
                }
            }

            AddArtefactCopyMappingDestructivePaths(paths, cfg, plan, outputRoot, enforceRelativeDestination: false);
            return paths;
        }

        if (artefact.ArtefactType == ArtefactType.Packed || artefact.ArtefactType == ArtefactType.ScriptPacked)
        {
            bool scriptArtefact = artefact.ArtefactType == ArtefactType.ScriptPacked;
            var packedRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", "validation", plan.ModuleName);
            var requiredModulesRoot = ArtefactLayoutPathResolver.ResolveRequiredModulesRootForPacked(
                cfg,
                outputRoot,
                packedRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease);
            var modulesRoot = ArtefactLayoutPathResolver.ResolveModulesRootForPacked(
                cfg,
                outputRoot,
                packedRoot,
                requiredModulesRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease);

            if (cfg.DoNotClear != true)
            {
                paths.Add(new ArtefactDestructivePath(
                    outputRoot,
                    "direct output files",
                    ArtefactDestructivePathKind.DirectChildNonZipFiles));
            }

            paths.Add(new ArtefactDestructivePath(
                Path.Combine(outputRoot, ArtefactLayoutPathResolver.ResolveArtefactFileName(cfg, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease)),
                "zip output file",
                ArtefactDestructivePathKind.ExactFile));

            paths.Add(new ArtefactDestructivePath(
                scriptArtefact ? modulesRoot : Path.Combine(modulesRoot, plan.ModuleName),
                scriptArtefact ? "main packed script copy root" : "main packed module copy root",
                ArtefactDestructivePathKind.DirectoryTree));

            if (cfg.RequiredModules.Enabled == true)
            {
                foreach (var requiredModule in FilterRequiredModulesForArtefactValidation(plan.RequiredModulesForPackaging, cfg.RequiredModules.ExcludeModuleName))
                {
                    paths.Add(new ArtefactDestructivePath(
                        Path.Combine(requiredModulesRoot, requiredModule.ModuleName!),
                        "required packed module copy root",
                        ArtefactDestructivePathKind.DirectoryTree));
                }
            }

            AddArtefactCopyMappingDestructivePaths(
                paths,
                cfg,
                plan,
                packedRoot,
                enforceRelativeDestination: true);
        }

        return paths;
    }

    private static void AddArtefactCopyMappingDestructivePaths(
        List<ArtefactDestructivePath> paths,
        ArtefactConfiguration cfg,
        ModulePipelinePlan plan,
        string destinationRoot,
        bool enforceRelativeDestination)
    {
        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;

            paths.Add(new ArtefactDestructivePath(
                ResolveArtefactCopyOutputPath(mapping.Destination, destinationRoot, cfg.DestinationDirectoriesRelative == true, enforceRelativeDestination, plan),
                "directory copy mapping output",
                ArtefactDestructivePathKind.DirectoryTree));
        }

        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;

            paths.Add(new ArtefactDestructivePath(
                ResolveArtefactCopyOutputPath(mapping.Destination, destinationRoot, cfg.DestinationFilesRelative == true, enforceRelativeDestination, plan),
                "file copy mapping output",
                ArtefactDestructivePathKind.ExactFile));
        }
    }

    private static RequiredModuleReference[] FilterRequiredModulesForArtefactValidation(
        IReadOnlyList<RequiredModuleReference>? requiredModules,
        IReadOnlyList<string>? excludedModuleNames)
    {
        var excluded = new HashSet<string>(
            (excludedModuleNames ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return (requiredModules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .Where(module => !excluded.Contains(module.ModuleName!))
            .ToArray();
    }

    private static bool DoesDestructivePathAffectProtectedPath(
        ArtefactDestructivePath destructivePath,
        ReleaseProtectedPath protectedPath)
        => destructivePath.Kind switch
        {
            ArtefactDestructivePathKind.DirectoryTree => protectedPath.IsFile
                ? IsSameOrChildPath(destructivePath.Path, protectedPath.Path)
                : DoPathsOverlap(destructivePath.Path, protectedPath.Path),
            ArtefactDestructivePathKind.DirectChildNonZipFiles => protectedPath.IsFile
                && !IsZipFilePath(protectedPath.Path)
                && IsDirectChildPath(destructivePath.Path, protectedPath.Path),
            ArtefactDestructivePathKind.ExactFile => PathsEqual(destructivePath.Path, protectedPath.Path),
            _ => false
        };

    private static string ResolveArtefactCopyOutputPath(
        string? value,
        string destinationRoot,
        bool relativeToRoot,
        bool enforceRelativeDestination,
        ModulePipelinePlan plan)
    {
        var raw = ModulePathTokenFormatter.ReplacePathTokens(value ?? string.Empty, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(destinationRoot);

        if (enforceRelativeDestination && Path.IsPathRooted(raw))
            throw new InvalidOperationException($"Packed artefact copy destinations must be relative, but got rooted path '{raw}'.");

        if (relativeToRoot || !Path.IsPathRooted(raw))
        {
            var resolved = Path.GetFullPath(Path.Combine(destinationRoot, raw));
            if (enforceRelativeDestination && !IsSameOrChildPath(destinationRoot, resolved))
            {
                throw new InvalidOperationException(
                    $"Packed artefact copy destination '{raw}' resolves outside the temporary packing root '{Path.GetFullPath(destinationRoot)}'. Use a destination path that stays within the packed artefact payload.");
            }

            return resolved;
        }

        return Path.GetFullPath(raw);
    }

    private static bool IsDirectChildPath(string parentPath, string candidatePath)
    {
        var parent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidateParent = Path.GetDirectoryName(Path.GetFullPath(candidatePath))
            ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidateParent is not null &&
               string.Equals(parent, candidateParent, GetPathComparison(parent, candidateParent));
    }

    private static bool IsZipFilePath(string path)
        => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

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
                AddReleaseProtectedPath(paths, package, "project build package asset", isFile: true);

                var packageDirectory = string.IsNullOrWhiteSpace(package)
                    ? null
                    : Path.GetDirectoryName(Path.GetFullPath(package.Trim().Trim('"')));
                AddReleaseProtectedPath(paths, packageDirectory, "project build package directory");
            }

            foreach (var releaseZipPath in result.Result?.Release?.Projects.Select(static project => project.ReleaseZipPath) ?? Array.Empty<string?>())
            {
                AddReleaseProtectedPath(paths, releaseZipPath, "project build release zip asset", isFile: true);
            }
        }

        return paths;
    }

    private static void AddReleaseProtectedPath(
        List<ReleaseProtectedPath> paths,
        string? path,
        string label,
        bool isFile = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        paths.Add(new ReleaseProtectedPath(
            Path.GetFullPath(path!.Trim().Trim('"')),
            label,
            isFile));
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

        var moduleVersion = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var releaseVersion = ResolveRequestedPackageReleaseVersion(plan, state) ?? moduleVersion;
        var stageRoot = ResolveReleaseStageRoot(plan, plan.Release.Configuration);
        var releaseRoot = stageRoot ?? ResolveDefaultReleaseRoot(plan, state, publishId);
        var scriptArchiveRoot = ResolveScriptReleaseArchiveRoot(plan, state, releaseRoot, stageRoot is not null);
        var moduleAssets = CollectModuleReleaseAssets(
            state.ArtefactResults,
            publishId,
            scriptArchiveRoot);
        var packageAssets = CollectPackageReleaseAssets(state.ProjectBuildResults);
        var allAssets = moduleAssets.Concat(packageAssets).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        string[] finalAssets;
        if (string.IsNullOrWhiteSpace(stageRoot))
        {
            finalAssets = allAssets
                .Concat(WriteReleaseMetadata(plan, releaseRoot, allAssets, releaseVersion))
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
        }
        else
        {
            finalAssets = StageUnifiedReleaseAssets(plan, stageRoot!, moduleAssets, packageAssets, releaseVersion);
        }

        return new ModuleReleaseCoordinationResult
        {
            StageRoot = stageRoot ?? string.Empty,
            ModuleVersion = moduleVersion,
            ReleaseVersion = releaseVersion,
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
        ModulePipelineRunState state,
        Action remotePublishAttempted,
        IGitHubReleaseProgressReporter? gitHubProgress)
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
        var missingAsset = release.AssetPaths.FirstOrDefault(static asset => string.IsNullOrWhiteSpace(asset) || !File.Exists(asset));
        if (missingAsset is not null)
            throw new FileNotFoundException($"Unified GitHub release asset was not found: {missingAsset}", missingAsset);

        var owner = publish.UserName!.Trim();
        var repo = string.IsNullOrWhiteSpace(publish.RepositoryName) ? plan.ModuleName : publish.RepositoryName!.Trim();
        var tag = ModulePublisher.GetGitHubTag(publish, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease);
        var versionText = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var isPreRelease = !string.IsNullOrWhiteSpace(plan.PreRelease) && !publish.DoNotMarkAsPreRelease;
        var coordinatedReleaseId = ResolveSynchronizedGitHubReleaseId(state, owner, repo, tag);

        if (coordinatedReleaseId.HasValue)
        {
            _logger.Info(
                $"Reusing package-created GitHub release {coordinatedReleaseId.Value} for coordinated tag '{tag}'.");
        }

        _logger.Info($"Publishing unified GitHub release {owner}/{repo} tag '{tag}' with {release.AssetPaths.Length} asset(s).");
        remotePublishAttempted();
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
            ReuseExistingReleaseOnConflict = publish.ReuseExistingRelease || coordinatedReleaseId.HasValue,
            RequireExpectedExistingRelease = coordinatedReleaseId.HasValue,
            ExpectedExistingReleaseId = coordinatedReleaseId,
            ReplaceExistingAssets = publish.ReuseExistingRelease && publish.ReplaceExistingAssets,
            AssetFilePaths = release.AssetPaths,
            Progress = gitHubProgress
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

    private static long? ResolveSynchronizedGitHubReleaseId(
        ModulePipelineRunState state,
        string owner,
        string repository,
        string tag)
    {
        var releaseIds = state.SynchronizedReleaseCheckpoint?.GitHubReleases
            .Where(release =>
                string.Equals(release.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(release.Repository, repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(release.TagName, tag, StringComparison.OrdinalIgnoreCase))
            .Select(static release => release.ReleaseId)
            .Distinct()
            .ToArray() ?? Array.Empty<long>();

        return releaseIds.Length switch
        {
            0 => null,
            1 => releaseIds[0],
            _ => throw new InvalidOperationException(
                $"Coordinated release {owner}/{repository} tag '{tag}' is bound to multiple GitHub release identifiers. Preserve the checkpoint and inspect the incomplete release before retrying.")
        };
    }

    private static bool ShouldPublishUnifiedGitHubRelease(ModulePipelinePlan plan, PublishConfiguration publish)
        => plan.Release?.Configuration is not null &&
           publish.Destination == PublishDestination.GitHub;

    private readonly struct ArtefactDestructivePath
    {
        public ArtefactDestructivePath(string path, string label, ArtefactDestructivePathKind kind)
        {
            Path = System.IO.Path.GetFullPath(path);
            Label = label;
            Kind = kind;
        }

        public string Path { get; }
        public string Label { get; }
        public ArtefactDestructivePathKind Kind { get; }
    }

    private enum ArtefactDestructivePathKind
    {
        DirectoryTree,
        DirectChildNonZipFiles,
        ExactFile
    }

    private readonly struct ReleaseProtectedPath
    {
        public ReleaseProtectedPath(string path, string label, bool isFile)
        {
            Path = path;
            Label = label;
            IsFile = isFile;
        }

        public string Path { get; }
        public string Label { get; }
        public bool IsFile { get; }
    }
}
