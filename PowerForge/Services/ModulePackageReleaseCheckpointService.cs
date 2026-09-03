namespace PowerForge;

/// <summary>
/// Resolves module-owned package lanes and captures their immutable release plans.
/// </summary>
internal sealed class ModulePackageReleaseCheckpointService
{
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ILogger _logger;

    internal ModulePackageReleaseCheckpointService(
        ProjectBuildHostService? projectBuildHostService = null,
        ILogger? logger = null)
    {
        _projectBuildHostService = projectBuildHostService ?? new ProjectBuildHostService();
        _logger = logger ?? new NullLogger();
    }

    internal PowerForgeModulePackageReleaseCheckpoint[] Capture(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
        => Capture(ResolveLanes(releaseConfigPath, spec));

    internal PowerForgeModulePackageReleaseCheckpoint[] Capture(
        ModulePipelineConfigurationContext context)
        => Capture(ResolveLanes(context));

    private PowerForgeModulePackageReleaseCheckpoint[] Capture(
        IReadOnlyList<ModulePackageReleaseLane> resolvedLanes)
    {
        var checkpoints = new List<PowerForgeModulePackageReleaseCheckpoint>();
        foreach (var lane in resolvedLanes
                     .Where(static lane => lane.PublishNuget || lane.PublishGitHub))
        {
            var request = new ProjectBuildHostRequest
            {
                ConfigPath = lane.ResolutionConfigPath,
                ExecuteBuild = false,
                PlanOnly = true,
                UpdateVersions = false,
                Build = true,
                PublishNuget = false,
                PublishGitHub = false
            };
            var execution = lane.Reference is not null
                ? _projectBuildHostService.Execute(request, lane.Reference, lane.ConfigPath)
                : _projectBuildHostService.Execute(request, lane.Inline!, lane.ResolutionConfigPath);
            if (!execution.Success || execution.Result.Release is null)
            {
                throw new InvalidOperationException(
                    $"{lane.Name}: package release plan could not be checkpointed. {execution.ErrorMessage}");
            }

            checkpoints.Add(new PowerForgeModulePackageReleaseCheckpoint
            {
                Key = lane.Key,
                Name = lane.Name,
                ConfigPath = Path.GetFullPath(lane.ConfigPath),
                PublishNuget = lane.PublishNuget,
                PublishGitHub = lane.PublishGitHub,
                Release = execution.Result.Release
            });
        }

        return checkpoints.ToArray();
    }

    internal PowerForgeModulePackagePublicationResult[] PublishNuGet(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec,
        IEnumerable<PowerForgeModulePackageReleaseCheckpoint>? checkpoints,
        IEnumerable<PowerForgeReleaseAssetEntry>? releaseAssets,
        bool requireStagedAssets,
        Action? remotePublishAttempted,
        IProjectBuildProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var lanes = ResolveLanes(releaseConfigPath, spec)
            .Where(static lane => lane.PublishNuget)
            .ToArray();
        var publisher = new ProjectBuildPublishHostService(_logger);
        var publications = new List<PowerForgeModulePackagePublicationResult>(lanes.Length);
        foreach (var lane in lanes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpoint = Restore(lane, checkpoints);
            var configuration = lane.Reference is not null
                ? publisher.LoadConfiguration(lane.Reference, lane.ConfigPath)
                : publisher.LoadConfiguration(lane.Inline!, lane.ConfigPath);
            var stagedRelease = CreatePublicationRelease(
                checkpoint.Release,
                releaseAssets,
                requireStagedAssets);
            using var publicationSnapshot = ModulePackagePublicationSnapshot.Create(stagedRelease);
            var release = publicationSnapshot.Release;
            var publish = publisher.PublishNuGet(
                configuration,
                release,
                repositoryRoot: ResolveRepositoryRoot(releaseConfigPath, spec),
                remotePublishAttempted: () =>
                {
                    publicationSnapshot.ValidateUnchanged();
                    remotePublishAttempted?.Invoke();
                },
                progress: progress,
                cancellationToken: cancellationToken);
            publicationSnapshot.ValidateUnchanged();
            var result = new PowerForgeModulePackagePublicationResult
            {
                Name = checkpoint.Name,
                Success = publish.Success,
                ErrorMessage = publish.ErrorMessage,
                PublishSource = release.PublishSource,
                PublishedPackages = publicationSnapshot.ResolveOriginalPaths(release.PublishedPackages),
                SkippedDuplicatePackages = publicationSnapshot.ResolveOriginalPaths(release.SkippedDuplicatePackages),
                FailedPackages = publicationSnapshot.ResolveOriginalPaths(release.FailedPackages)
            };
            publications.Add(result);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? $"NuGet publication failed for module package lane '{checkpoint.Name}'.");
        }

        return publications.ToArray();
    }

    internal static DotNetRepositoryReleaseResult CreatePublicationRelease(
        DotNetRepositoryReleaseResult source,
        IEnumerable<PowerForgeReleaseAssetEntry>? releaseAssets,
        bool requireStagedAssets)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (!source.Success)
            throw new InvalidOperationException(source.ErrorMessage ?? "The module package release checkpoint was not successful.");

        var stagedBySource = (releaseAssets ?? Array.Empty<PowerForgeReleaseAssetEntry>())
            .Where(static asset =>
                asset.Category == PowerForgeReleaseAssetCategory.Package &&
                asset.IsFinalPackageOutput &&
                !string.IsNullOrWhiteSpace(asset.Path) &&
                !string.IsNullOrWhiteSpace(asset.StagedPath))
            .GroupBy(asset => Path.GetFullPath(asset.Path), PathComparer)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single(),
                PathComparer);
        var clone = new DotNetRepositoryReleaseResult
        {
            Success = source.Success,
            ErrorMessage = source.ErrorMessage,
            ResolvedVersion = source.ResolvedVersion,
            Projects = source.Projects.Select(project => new DotNetRepositoryProjectResult
            {
                ProjectName = project.ProjectName,
                CsprojPath = project.CsprojPath,
                PackageId = project.PackageId,
                IsPackable = project.IsPackable,
                OldVersion = project.OldVersion,
                NewVersion = project.NewVersion,
                Packages = project.Packages
                    .Select(path => ResolvePublicationPackagePath(path, stagedBySource, requireStagedAssets))
                    .ToList(),
                SymbolPackages = project.SymbolPackages
                    .Select(path => ResolvePublicationPackagePath(path, stagedBySource, requireStagedAssets))
                    .ToList(),
                ReleaseZipPath = project.ReleaseZipPath,
                PackageBuildDuration = project.PackageBuildDuration,
                ErrorMessage = project.ErrorMessage
            }).ToList()
        };
        foreach (var version in source.ResolvedVersionsByProject)
            clone.ResolvedVersionsByProject[version.Key] = version.Value;
        return clone;
    }

    private static string ResolvePublicationPackagePath(
        string path,
        IReadOnlyDictionary<string, PowerForgeReleaseAssetEntry> stagedBySource,
        bool requireStagedAssets)
    {
        var fullPath = Path.GetFullPath(path);
        if (stagedBySource.TryGetValue(fullPath, out var stagedAsset))
        {
            var stagedPath = Path.GetFullPath(stagedAsset.StagedPath!);
            if (!File.Exists(stagedPath))
                throw new FileNotFoundException($"The staged package artifact was not found: {stagedPath}", stagedPath);
            if (requireStagedAssets && string.IsNullOrWhiteSpace(stagedAsset.StagedSha256))
            {
                throw new InvalidOperationException(
                    $"The validated staged package artifact has no captured SHA-256 digest: '{stagedPath}'.");
            }
            if (!string.IsNullOrWhiteSpace(stagedAsset.StagedSha256))
            {
                var actualSha256 = ComputeSha256(stagedPath);
                if (!string.Equals(actualSha256, stagedAsset.StagedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The staged package artifact changed after release staging: '{stagedPath}'.");
                }
            }
            return stagedPath;
        }

        if (requireStagedAssets)
        {
            throw new InvalidOperationException(
                $"The validated staged release does not contain package artifact '{fullPath}'.");
        }
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"The package artifact was not found: {fullPath}", fullPath);
        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha256.ComputeHash(stream).Select(static value => value.ToString("x2")));
    }

    private static string ResolveRepositoryRoot(string releaseConfigPath, PowerForgeReleaseSpec spec)
    {
        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        return string.IsNullOrWhiteSpace(spec.Module?.RepositoryRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, spec.Module!.RepositoryRoot!);
    }

    private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\'
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static PowerForgeModulePackageReleaseCheckpoint Restore(
        ModulePackageReleaseLane lane,
        IEnumerable<PowerForgeModulePackageReleaseCheckpoint>? checkpoints)
    {
        var normalizedConfigPath = Path.GetFullPath(lane.ConfigPath);
        var matches = (checkpoints ?? [])
            .Where(checkpoint =>
                !string.IsNullOrWhiteSpace(checkpoint.Key)
                    ? string.Equals(checkpoint.Key, lane.Key, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(checkpoint.Name, lane.Name, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(
                          Path.GetFullPath(checkpoint.ConfigPath),
                          normalizedConfigPath,
                          StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"{lane.Name}: the signed build checkpoint does not contain its package release plan."),
            _ => throw new InvalidOperationException(
                $"{lane.Name}: the signed build checkpoint contains duplicate package release plans.")
        };
    }

    internal static IReadOnlyList<ModulePackageReleaseLane> ResolveLanes(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
    {
        if (spec.Module?.IncludesPackages != true)
            return [];
        if (string.IsNullOrWhiteSpace(spec.Module.ConfigPath))
            return [];

        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = string.IsNullOrWhiteSpace(spec.Module.RepositoryRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, spec.Module.RepositoryRoot!);
        var moduleConfigPath = PathTokenProtection.GetFullPath(repositoryRoot, spec.Module.ConfigPath!);
        var context = new ModulePipelineConfigurationService().Load(moduleConfigPath);
        return ResolveLanes(context);
    }

    internal static IReadOnlyList<ModulePackageReleaseLane> ResolveLanes(
        ModulePipelineConfigurationContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var moduleConfigPath = context.ConfigPath;
        var lanes = new List<ModulePackageReleaseLane>();
        var segments = context.Spec.Segments ?? [];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            switch (segment)
            {
                case ConfigurationProjectBuildSegment project when project.Configuration.Enabled:
                {
                    var configPath = ModulePipelineConfigurationService.ResolveProjectBuildConfigurationPath(
                        context,
                        project.Configuration);
                    var publish = new ProjectBuildSupportService(new NullLogger()).LoadConfig(configPath);
                    lanes.Add(new ModulePackageReleaseLane(
                        $"ProjectBuild:{index}",
                        project.Configuration.Name ?? Path.GetFileNameWithoutExtension(configPath),
                        configPath,
                        configPath,
                        project.Configuration,
                        null,
                        project.Configuration.PublishNuget ?? (publish.PublishNuget == true),
                        project.Configuration.PublishGitHub ?? (publish.PublishGitHub == true)));
                    break;
                }
                case ConfigurationPackageBuildSegment package when package.Configuration.Enabled:
                    lanes.Add(new ModulePackageReleaseLane(
                        $"PackageBuild:{index}",
                        package.Configuration.Name ?? "Inline package build",
                        moduleConfigPath,
                        Path.Combine(context.ProjectRoot, Path.GetFileName(moduleConfigPath)),
                        null,
                        package.Configuration,
                        package.Configuration.PublishNuget == true,
                        package.Configuration.PublishGitHub == true));
                    break;
            }
        }

        return lanes;
    }
}

internal sealed class ModulePackagePublicationSnapshot : IDisposable
{
    private readonly string _rootPath;
    private readonly Dictionary<string, string> _originalBySnapshot;
    private readonly Dictionary<string, string> _sha256BySnapshot;
    private bool _disposed;

    private ModulePackagePublicationSnapshot(
        string rootPath,
        DotNetRepositoryReleaseResult release,
        Dictionary<string, string> originalBySnapshot,
        Dictionary<string, string> sha256BySnapshot)
    {
        _rootPath = rootPath;
        Release = release;
        _originalBySnapshot = originalBySnapshot;
        _sha256BySnapshot = sha256BySnapshot;
    }

    internal DotNetRepositoryReleaseResult Release { get; }

    internal static ModulePackagePublicationSnapshot Create(DotNetRepositoryReleaseResult release)
    {
        if (release is null)
            throw new ArgumentNullException(nameof(release));

        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "PowerForge",
            "module-package-publication",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var snapshotByOriginal = new Dictionary<string, string>(PathComparer);
        var originalBySnapshot = new Dictionary<string, string>(PathComparer);
        var sha256BySnapshot = new Dictionary<string, string>(PathComparer);
        try
        {
            string Snapshot(string path)
            {
                var originalPath = Path.GetFullPath(path);
                if (snapshotByOriginal.TryGetValue(originalPath, out var existing))
                    return existing;
                if (!File.Exists(originalPath))
                    throw new FileNotFoundException($"The package publication input was not found: {originalPath}", originalPath);

                var expectedSha256 = ComputeSha256(originalPath);
                var snapshotPath = Path.Combine(
                    rootPath,
                    snapshotByOriginal.Count.ToString("D4"),
                    Path.GetFileName(originalPath));
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
                File.Copy(originalPath, snapshotPath, overwrite: false);
                var sourceSha256AfterCopy = ComputeSha256(originalPath);
                var snapshotSha256 = ComputeSha256(snapshotPath);
                if (!string.Equals(expectedSha256, sourceSha256AfterCopy, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expectedSha256, snapshotSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The staged package changed while its private publication snapshot was created: '{originalPath}'.");
                }

                File.SetAttributes(snapshotPath, File.GetAttributes(snapshotPath) | FileAttributes.ReadOnly);
                snapshotByOriginal[originalPath] = snapshotPath;
                originalBySnapshot[snapshotPath] = originalPath;
                sha256BySnapshot[snapshotPath] = snapshotSha256;
                return snapshotPath;
            }

            foreach (var project in release.Projects)
            {
                project.Packages = project.Packages.Select(Snapshot).ToList();
                project.SymbolPackages = project.SymbolPackages.Select(Snapshot).ToList();
            }

            return new ModulePackagePublicationSnapshot(
                rootPath,
                release,
                originalBySnapshot,
                sha256BySnapshot);
        }
        catch
        {
            DeleteSnapshot(rootPath);
            throw;
        }
    }

    internal void ValidateUnchanged()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ModulePackagePublicationSnapshot));

        foreach (var entry in _sha256BySnapshot)
        {
            if (!File.Exists(entry.Key) ||
                !string.Equals(ComputeSha256(entry.Key), entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private package publication snapshot changed before publication completed: '{entry.Key}'.");
            }
        }
    }

    internal string[] ResolveOriginalPaths(IEnumerable<string> paths)
        => paths.Select(path =>
        {
            var fullPath = Path.GetFullPath(path);
            return _originalBySnapshot.TryGetValue(fullPath, out var originalPath)
                ? originalPath
                : fullPath;
        }).ToArray();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DeleteSnapshot(_rootPath);
    }

    private static void DeleteSnapshot(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return;

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(path, FileAttributes.Normal); }
            catch { }
        }
        try { Directory.Delete(rootPath, recursive: true); }
        catch { }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha256.ComputeHash(stream).Select(static value => value.ToString("x2")));
    }

    private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\'
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>
/// Resolved module-owned package lane used by checkpoint capture and staged publication.
/// </summary>
internal sealed class ModulePackageReleaseLane
{
    internal ModulePackageReleaseLane(
        string key,
        string name,
        string configPath,
        string resolutionConfigPath,
        ProjectBuildConfigurationReference? reference,
        PackageBuildConfiguration? inline,
        bool publishNuget,
        bool publishGitHub)
    {
        Key = key;
        Name = name;
        ConfigPath = configPath;
        ResolutionConfigPath = resolutionConfigPath;
        Reference = reference;
        Inline = inline;
        PublishNuget = publishNuget;
        PublishGitHub = publishGitHub;
    }

    internal string Key { get; }

    internal string Name { get; }

    internal string ConfigPath { get; }

    internal string ResolutionConfigPath { get; }

    internal ProjectBuildConfigurationReference? Reference { get; }

    internal PackageBuildConfiguration? Inline { get; }

    internal bool PublishNuget { get; }

    internal bool PublishGitHub { get; }
}
