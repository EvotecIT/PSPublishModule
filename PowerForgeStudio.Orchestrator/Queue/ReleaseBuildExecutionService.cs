using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseBuildExecutionService : IReleaseBuildExecutionService
{
    private readonly RepositoryCatalogScanner _catalogScanner;
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ProjectBuildCommandHostService _projectBuildCommandHostService;
    private readonly ModuleBuildHostService _moduleBuildHostService;
    private readonly Func<string, PowerForgeReleaseRequest, PowerForgeReleaseResult> _executeUnifiedReleaseBuild;

    public ReleaseBuildExecutionService()
        : this(new RepositoryCatalogScanner(), new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ModuleBuildHostService())
    {
    }

    internal ReleaseBuildExecutionService(
        RepositoryCatalogScanner catalogScanner,
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ModuleBuildHostService moduleBuildHostService,
        Func<string, PowerForgeReleaseRequest, PowerForgeReleaseResult>? executeUnifiedReleaseBuild = null)
    {
        _catalogScanner = catalogScanner;
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _moduleBuildHostService = moduleBuildHostService;
        _executeUnifiedReleaseBuild = executeUnifiedReleaseBuild ?? ExecuteUnifiedReleaseBuild;
    }

    public async Task<ReleaseBuildExecutionResult> ExecuteAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var repository = _catalogScanner.InspectRepository(repositoryRoot);
        if (!repository.IsReleaseManaged)
        {
            return new ReleaseBuildExecutionResult(
                RootPath: repositoryRoot,
                Succeeded: false,
                Summary: "No supported build contract was detected for this repository.",
                DurationSeconds: 0,
                AdapterResults: []);
        }

        var results = new List<ReleaseBuildAdapterResult>();
        var startedAt = DateTimeOffset.UtcNow;
        var directModuleConfigPath =
            string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath) &&
            string.Equals(Path.GetExtension(repository.ModuleBuildScriptPath), ".json", StringComparison.OrdinalIgnoreCase)
                ? repository.ModuleBuildScriptPath
                : null;
        var directModuleConfigFingerprint = !string.IsNullOrWhiteSpace(directModuleConfigPath)
            ? UnifiedReleaseConfigFingerprint.ComputeModuleConfig(directModuleConfigPath!)
            : null;
        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
        {
            var configPath = repository.UnifiedReleaseConfigPath!;
            var configFingerprint = UnifiedReleaseConfigFingerprint.Compute(configPath);
            var moduleStagingPath = ResolveModuleCheckpointStagingPath(repository.Name);
            var unifiedRequest = CreateUnifiedReleaseBuildRequest(
                configPath,
                PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
                moduleStagingPath);
            unifiedRequest.CancellationToken = cancellationToken;
            var unified = await Task.Run(
                () => _executeUnifiedReleaseBuild(configPath, unifiedRequest),
                cancellationToken).ConfigureAwait(false);
            var moduleExportCheckpoint =
                await CaptureScriptModuleExportedConfigFingerprintAsync(
                    repository,
                    unified,
                    cancellationToken).ConfigureAwait(false);
            if (moduleExportCheckpoint.PackagePlans.Length > 0)
                unified.ModulePackagePlans = moduleExportCheckpoint.PackagePlans;
            var completedFingerprint = UnifiedReleaseConfigFingerprint.Compute(configPath);
            if (!string.Equals(configFingerprint, completedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Unified release configuration changed while the build was running. Rebuild from the updated contract before signing.");
            }

            results.AddRange(CreateUnifiedAdapterResults(repository, unified, DateTimeOffset.UtcNow - startedAt));
            return ReleaseQueueExecutionResultFactory.CreateBuildResult(
                repositoryRoot,
                DateTimeOffset.UtcNow - startedAt,
                results,
                SerializeUnifiedCheckpoint(unified),
                configFingerprint,
                moduleExportedConfigSha256: moduleExportCheckpoint.Fingerprint);
        }

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            results.Add(await ExecuteProjectBuildAsync(repository, cancellationToken));
        }

        PowerForgeReleaseResult? directModuleCheckpoint = null;
        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            var moduleResult = await ExecuteModuleBuildAsync(repository, cancellationToken);
            results.Add(moduleResult);
            if (!string.IsNullOrWhiteSpace(directModuleConfigPath) &&
                string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath) &&
                moduleResult.Succeeded)
            {
                directModuleCheckpoint = CreateDirectModulePackageCheckpoint(
                    directModuleConfigPath!);
            }
        }

        if (!string.IsNullOrWhiteSpace(directModuleConfigPath))
        {
            var completedFingerprint = UnifiedReleaseConfigFingerprint.ComputeModuleConfig(directModuleConfigPath!);
            if (!string.Equals(directModuleConfigFingerprint, completedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Module build configuration changed while the build was running. Rebuild from the updated contract before signing.");
            }
        }

        return ReleaseQueueExecutionResultFactory.CreateBuildResult(
            repositoryRoot,
            DateTimeOffset.UtcNow - startedAt,
            results,
            unifiedReleaseStateJson: directModuleCheckpoint is null
                ? null
                : SerializeUnifiedCheckpoint(directModuleCheckpoint),
            moduleBuildConfigSha256: directModuleConfigFingerprint);
    }

    internal static PowerForgeReleaseRequest CreateUnifiedReleaseBuildRequest(
        string configPath,
        string moduleHostPath,
        string moduleStagingPath)
    {
        var request = new PowerForgeReleaseRequest {
            ConfigPath = configPath,
            ModuleHostPath = moduleHostPath,
            PublishNuget = false,
            PublishProjectGitHub = false,
            PublishToolGitHub = false,
            ModuleRunMode = ConfigurationGateMode.Build,
            ModuleStagingPath = moduleStagingPath,
            ModuleNoSign = true,
            ModuleSkipInstall = true,
            EnableSigning = false,
            SkipAppleApps = true,
            SubmitWinget = false
        };

        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        if (spec.AppleApps is not null)
        {
            request.SkipAppleApps = false;
            request.CheckpointAppleApps = true;
            request.PlanOnly =
                spec.Module is null &&
                spec.Packages is null &&
                spec.Tools is null &&
                spec.WorkspaceValidation is null;
        }

        return request;
    }

    private static PowerForgeReleaseResult ExecuteUnifiedReleaseBuild(string configPath, PowerForgeReleaseRequest request)
    {
        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        return new PowerForgeReleaseService(new NullLogger()).Execute(spec, request);
    }

    private static IReadOnlyList<ReleaseBuildAdapterResult> CreateUnifiedAdapterResults(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PowerForgeReleaseResult unified,
        TimeSpan duration)
    {
        var results = new List<ReleaseBuildAdapterResult>();
        if (unified.Packages is not null)
        {
            var artifacts = CollectProjectArtifacts(unified.Packages);
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.ProjectBuild,
                unified.Packages.Success,
                unified.Packages.Success ? "Unified package lane completed with publishing disabled." : "Unified package lane failed.",
                unified.Packages.Success ? 0 : 1,
                Math.Round(duration.TotalSeconds, 2),
                artifacts.Directories,
                artifacts.Files,
                ErrorTail: TrimTail(unified.Packages.ErrorMessage ?? unified.Packages.Result.Release?.ErrorMessage)));
        }

        if (unified.ModulePlan is not null)
        {
            var buildInput = unified.ModulePlan.ConfigPath ?? repository.ModuleBuildScriptPath;
            var explicitModuleAssets = (unified.ModuleAssets ?? [])
                .Concat(string.IsNullOrWhiteSpace(unified.ModulePlan.StagingPath)
                    ? []
                    : [unified.ModulePlan.StagingPath!])
                .ToArray();
            var artifacts = explicitModuleAssets.Length > 0
                ? CollectExplicitArtifacts(explicitModuleAssets)
                : !string.IsNullOrWhiteSpace(buildInput)
                ? CollectModuleArtifacts(
                    repository.RootPath,
                    buildInput!,
                    unified.ModulePlan.StagingPath)
                : CollectExplicitArtifacts(unified.ModuleAssets ?? []);
            if (unified.ModulePlan.IncludesPackages &&
                !string.IsNullOrWhiteSpace(unified.ModulePlan.ConfigPath))
            {
                artifacts = MergeArtifactCollections(
                    artifacts,
                    CollectModulePackageArtifacts(unified.ModulePlan.ConfigPath!));
            }
            var moduleSucceeded = unified.Module?.Succeeded == true;
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.ModuleBuild,
                moduleSucceeded,
                moduleSucceeded ? "Unified module lane completed with publishing, signing, and install disabled." : "Unified module lane failed.",
                moduleSucceeded ? 0 : unified.Module?.ExitCode ?? 1,
                Math.Round(duration.TotalSeconds, 2),
                artifacts.Directories,
                artifacts.Files,
                OutputTail: TrimTail(unified.Module?.StandardOutput),
                ErrorTail: TrimTail(unified.Module?.StandardError ?? unified.ErrorMessage)));
        }

        if (unified.ToolPlan is not null || unified.DotNetToolPlan is not null || unified.Tools is not null || unified.DotNetTools is not null)
        {
            var artifacts = CollectToolArtifacts(unified);
            var toolSucceeded = unified.Tools?.Success ?? unified.DotNetTools?.Succeeded ?? unified.Success;
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.ToolBuild,
                toolSucceeded,
                toolSucceeded ? "Unified executable/tool lane completed with publishing disabled." : "Unified executable/tool lane failed.",
                toolSucceeded ? 0 : 1,
                Math.Round(duration.TotalSeconds, 2),
                artifacts.Directories,
                artifacts.Files,
                ErrorTail: TrimTail(unified.Tools?.ErrorMessage ?? unified.DotNetTools?.ErrorMessage ?? unified.ErrorMessage)));
        }

        if (unified.AppleAppPlan is not null)
        {
            var artifacts = CollectExplicitArtifacts(
                unified.AppleAppPlan.Apps
                    .Where(static app => app.Upload)
                    .Select(static app => app.ArchivePath));
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.AppleBuild,
                unified.Success,
                unified.Success ? "Unified Apple release plan checkpointed without executing external actions." : "Unified Apple release planning failed.",
                unified.Success ? 0 : 1,
                Math.Round(duration.TotalSeconds, 2),
                artifacts.Directories,
                artifacts.Files,
                ErrorTail: TrimTail(unified.ErrorMessage)));
        }

        if (results.Count == 0 &&
            (unified.WorkspaceValidation is not null || unified.WorkspaceValidationPlan is not null))
        {
            var workspaceSucceeded = unified.WorkspaceValidation?.Succeeded ?? unified.Success;
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.ProjectBuild,
                workspaceSucceeded,
                workspaceSucceeded ? "Unified workspace validation completed." : "Unified workspace validation failed.",
                workspaceSucceeded ? 0 : 1,
                Math.Round(duration.TotalSeconds, 2),
                [],
                [],
                ErrorTail: TrimTail(unified.ErrorMessage)));
        }

        var releaseMetadata = CollectExplicitArtifacts(
            [unified.ReleaseManifestPath ?? string.Empty, unified.ReleaseChecksumsPath ?? string.Empty]);
        if (releaseMetadata.Directories.Count > 0 || releaseMetadata.Files.Count > 0)
        {
            if (results.Count == 0)
            {
                results.Add(new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ProjectBuild,
                    unified.Success,
                    unified.Success
                        ? "Unified release metadata checkpointed for signing."
                        : "Unified release metadata generation failed.",
                    unified.Success ? 0 : 1,
                    Math.Round(duration.TotalSeconds, 2),
                    releaseMetadata.Directories,
                    releaseMetadata.Files,
                    ErrorTail: TrimTail(unified.ErrorMessage)));
            }
            else
            {
                var first = results[0];
                var artifacts = MergeArtifactCollections(
                    new ArtifactCollection(first.ArtifactDirectories, first.ArtifactFiles),
                    releaseMetadata);
                results[0] = first with
                {
                    ArtifactDirectories = artifacts.Directories,
                    ArtifactFiles = artifacts.Files
                };
            }
        }

        if (results.Count == 0)
        {
            results.Add(new ReleaseBuildAdapterResult(
                ReleaseBuildAdapterKind.ProjectBuild,
                false,
                "Unified release did not produce any build lane.",
                1,
                Math.Round(duration.TotalSeconds, 2),
                [],
                [],
                ErrorTail: TrimTail(unified.ErrorMessage)));
        }

        return results;
    }

    private static string SerializeUnifiedCheckpoint(PowerForgeReleaseResult unified)
    {
        var checkpoint = JsonSerializer.Deserialize<PowerForgeReleaseResult>(JsonSerializer.Serialize(unified))
                         ?? throw new InvalidOperationException("Unified release state could not be cloned for checkpoint persistence.");
        if (checkpoint.DotNetToolPlan is not null)
        {
            DotNetPublishPlanRedactor.RedactInPlace(checkpoint.DotNetToolPlan);
        }

        return JsonSerializer.Serialize(checkpoint);
    }

    private static PowerForgeReleaseResult? CreateDirectModulePackageCheckpoint(
        string configPath)
    {
        var context = new ModulePipelineConfigurationService().Load(configPath);
        var packagePlans = new ModulePackageReleaseCheckpointService().Capture(context);
        if (packagePlans.Length == 0)
            return null;

        var preRelease = (context.Spec.Segments ?? [])
            .OfType<ConfigurationManifestSegment>()
            .Select(static segment => segment.Configuration?.Prerelease)
            .LastOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = configPath,
            ModulePlan = new PowerForgeModuleReleasePlanSummary
            {
                ModuleName = context.Spec.Build.Name,
                ConfigPath = configPath,
                IncludesPackages = true,
                IncludesProjectPackages = true,
                ModuleVersion = context.EffectiveVersion,
                PreReleaseTag = preRelease
            },
            ModulePackagePlans = packagePlans
        };
    }

    private async Task<ReleaseBuildAdapterResult> ExecuteProjectBuildAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = RepositoryPlanPreviewService.ResolveProjectConfigPath(scriptPath, repository.RootPath);

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var execution = _projectBuildHostService.Execute(new ProjectBuildHostRequest {
                ConfigPath = configPath,
                ExecuteBuild = true,
                PlanOnly = false,
                UpdateVersions = false,
                Build = true,
                PublishNuget = false,
                PublishGitHub = false
            });
            var artifactInfo = CollectProjectArtifacts(execution);

            return new ReleaseBuildAdapterResult(
                AdapterKind: ReleaseBuildAdapterKind.ProjectBuild,
                Succeeded: execution.Success,
                Summary: execution.Success ? "Project build completed with publish disabled." : "Project build failed.",
                ExitCode: execution.Success ? 0 : 1,
                DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
                ArtifactDirectories: artifactInfo.Directories,
                ArtifactFiles: artifactInfo.Files,
                OutputTail: null,
                ErrorTail: TrimTail(execution.ErrorMessage ?? execution.Result.Release?.ErrorMessage));
        }

        var powerShellExecution = await _projectBuildCommandHostService.ExecuteBuildAsync(new ProjectBuildCommandBuildRequest {
            RepositoryRoot = repository.RootPath,
            ConfigPath = configPath,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
        }, cancellationToken);
        var fallbackArtifactInfo = CollectProjectArtifacts(repository.RootPath);
        var succeeded = powerShellExecution.Succeeded;

        return new ReleaseBuildAdapterResult(
            AdapterKind: ReleaseBuildAdapterKind.ProjectBuild,
            Succeeded: succeeded,
            Summary: succeeded ? "Project build completed with publish disabled." : "Project build failed.",
            ExitCode: powerShellExecution.ExitCode,
            DurationSeconds: Math.Round(powerShellExecution.Duration.TotalSeconds, 2),
            ArtifactDirectories: fallbackArtifactInfo.Directories,
            ArtifactFiles: fallbackArtifactInfo.Files,
            OutputTail: TrimTail(powerShellExecution.StandardOutput),
            ErrorTail: TrimTail(powerShellExecution.StandardError));
    }

    private async Task<ReleaseBuildAdapterResult> ExecuteModuleBuildAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var buildInputPath = repository.ModuleBuildScriptPath!;
        var configBacked = string.Equals(Path.GetExtension(buildInputPath), ".json", StringComparison.OrdinalIgnoreCase);
        var modulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath();
        var stagingPath = ResolveModuleCheckpointStagingPath(repository.Name);
        var execution = await _moduleBuildHostService.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = repository.RootPath,
            ConfigPath = configBacked ? buildInputPath : null,
            ScriptPath = configBacked ? null : buildInputPath,
            ModulePath = modulePath,
            Framework = configBacked ? "auto" : null,
            RunMode = configBacked ? ConfigurationGateMode.Build : null,
            StagingPath = stagingPath,
            IncludeProjectPackages = string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath),
            SkipInstall = configBacked,
            NoSign = configBacked
        }, cancellationToken);
        var artifactInfo = CollectModuleArtifacts(repository.RootPath, buildInputPath, stagingPath);
        var succeeded = execution.Succeeded;

        return new ReleaseBuildAdapterResult(
            AdapterKind: ReleaseBuildAdapterKind.ModuleBuild,
            Succeeded: succeeded,
            Summary: succeeded ? "Module build completed with signing disabled and install skipped." : "Module build failed.",
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            ArtifactDirectories: artifactInfo.Directories,
            ArtifactFiles: artifactInfo.Files,
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private static ArtifactCollection CollectProjectArtifacts(ProjectBuildHostExecutionResult execution)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddArtifactDirectory(execution.StagingPath, directories);
        AddArtifactDirectory(execution.OutputPath, directories);
        AddArtifactDirectory(execution.ReleaseZipOutputPath, directories);
        AddArtifactDirectory(Path.Combine(execution.RootPath, "Artefacts", "ProjectBuild"), directories);

        AddReleaseArtifactFiles(execution.Result.Release, files);
        CollectArtifactFiles(directories, files, includePackages: false);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ArtifactCollection CollectProjectArtifacts(string repositoryRoot)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddArtifactDirectory(Path.Combine(repositoryRoot, "Artefacts", "ProjectBuild"), directories);
        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ArtifactCollection CollectModuleArtifacts(
        string repositoryRoot,
        string buildInputPath,
        string? stagingPath = null)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateDirectories = string.Equals(Path.GetExtension(buildInputPath), ".json", StringComparison.OrdinalIgnoreCase) &&
                                   new ModulePipelineConfigurationService().TryLoad(buildInputPath, out var context)
            ? context!.ArtifactPaths
                .Concat(context.PackageArtifactPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[]
            {
                Path.Combine(repositoryRoot, "Artefacts", "Unpacked"),
                Path.Combine(repositoryRoot, "Artefacts", "Packed"),
                Path.Combine(repositoryRoot, "Module", "Artefacts", "Unpacked"),
                Path.Combine(repositoryRoot, "Module", "Artefacts", "Packed")
            };
        foreach (var candidateDirectory in candidateDirectories)
        {
            AddModuleArtifactDirectory(candidateDirectory, directories);
        }
        if (!string.IsNullOrWhiteSpace(stagingPath))
            AddModuleArtifactDirectory(stagingPath!, directories);

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string ResolveModuleCheckpointStagingPath(string repositoryName)
    {
        var root = Path.GetDirectoryName(
            PowerForgeStudioHostPaths.GetRuntimeFilePath(
                repositoryName,
                "module-staging",
                "checkpoint.marker"));
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("PowerForge Studio module staging path could not be resolved.");
        return Path.Combine(root, Guid.NewGuid().ToString("N"));
    }

    private static ArtifactCollection CollectModulePackageArtifacts(string configPath)
    {
        var context = new ModulePipelineConfigurationService().Load(configPath);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in context.PackageArtifactPaths)
            AddModuleArtifactDirectory(path, directories);

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(
            directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ArtifactCollection MergeArtifactCollections(
        ArtifactCollection first,
        ArtifactCollection second)
        => new(
            first.Directories
                .Concat(second.Directories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            first.Files
                .Concat(second.Files)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList());

    private static ArtifactCollection CollectExplicitArtifacts(IEnumerable<string> paths)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths ?? [])
        {
            if (Directory.Exists(path))
                directories.Add(path);
            else if (File.Exists(path))
                files.Add(path);
        }
        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ArtifactCollection CollectToolArtifacts(PowerForgeReleaseResult unified)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in unified.ReleaseAssetEntries.Where(entry =>
                     entry.Category is not PowerForgeReleaseAssetCategory.Module and not PowerForgeReleaseAssetCategory.Package))
        {
            paths.Add(entry.StagedPath ?? entry.Path);
        }
        foreach (var artifact in unified.Tools?.Artefacts ?? [])
        {
            paths.Add(artifact.OutputPath);
            if (!string.IsNullOrWhiteSpace(artifact.ZipPath))
                paths.Add(artifact.ZipPath!);
        }
        foreach (var artifact in unified.DotNetTools?.Artefacts ?? [])
        {
            paths.Add(artifact.OutputDir);
            if (!string.IsNullOrWhiteSpace(artifact.ZipPath))
                paths.Add(artifact.ZipPath!);
        }
        return CollectExplicitArtifacts(paths);
    }

    private static void AddModuleArtifactDirectory(string path, ISet<string> directories)
    {
        if (Directory.Exists(path))
        {
            directories.Add(path);
            return;
        }

        var currentArtifact = PathTokenCandidateResolver.ResolveExistingPaths(path)
            .Where(Directory.Exists)
            .OrderByDescending(PathTokenCandidateResolver.GetLatestWriteTimeUtc)
            .ThenByDescending(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (currentArtifact is not null)
        {
            directories.Add(currentArtifact);
        }
    }

    private static void AddArtifactDirectory(string? path, ISet<string> directories)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            directories.Add(path);
        }
    }

    private static void AddReleaseArtifactFiles(DotNetRepositoryReleaseResult? release, ISet<string> files)
    {
        if (release is null)
        {
            return;
        }

        foreach (var package in release.Projects.SelectMany(project => project.Packages).Where(File.Exists))
        {
            files.Add(package);
        }

        foreach (var zip in release.Projects.Select(project => project.ReleaseZipPath).Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path!)))
        {
            files.Add(zip!);
        }
    }

    private static void CollectArtifactFiles(
        IEnumerable<string> directories,
        ISet<string> files,
        bool includePackages = true)
    {
        var extensions = includePackages
            ? new[] { "*.nupkg", "*.snupkg", "*.zip", "*.psd1", "*.psm1", "*.dll" }
            : new[] { "*.zip", "*.psd1", "*.psm1", "*.dll" };
        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                foreach (var file in Directory.EnumerateFiles(directory, extension, SearchOption.AllDirectories))
                {
                    files.Add(file);
                }
            }
        }
    }

    private async Task<ScriptModuleExportCheckpoint> CaptureScriptModuleExportedConfigFingerprintAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PowerForgeReleaseResult unified,
        CancellationToken cancellationToken)
    {
        var scriptPath = unified.ModulePlan?.ScriptPath;
        if (string.IsNullOrWhiteSpace(scriptPath))
            return new ScriptModuleExportCheckpoint(null, []);

        var outputPath = PowerForgeStudioHostPaths.GetRuntimeFilePath(
            repository.Name,
            "module-publish",
            "powerforge.publish.json");
        try
        {
            var execution = await _moduleBuildHostService.ExportPipelineJsonAsync(
                new ModuleBuildHostExportRequest {
                    RepositoryRoot = repository.RootPath,
                    ScriptPath = scriptPath!,
                    ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
                    OutputPath = outputPath
                },
                cancellationToken).ConfigureAwait(false);
            if (!execution.Succeeded || !File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Module publish configuration checkpoint export failed for '{scriptPath}' (exit {execution.ExitCode}).");
            }

            var context = new ModulePipelineConfigurationService().Load(outputPath);
            var packagePlans = unified.ModulePlan?.IncludesProjectPackages == true
                ? new ModulePackageReleaseCheckpointService().Capture(context)
                : [];
            return new ScriptModuleExportCheckpoint(
                UnifiedReleaseConfigFingerprint.ComputeModuleConfig(outputPath),
                packagePlans);
        }
        finally
        {
            try { File.Delete(outputPath); } catch { }
        }
    }

    private sealed record ScriptModuleExportCheckpoint(
        string? Fingerprint,
        PowerForgeModulePackageReleaseCheckpoint[] PackagePlans);

    private static string? TrimTail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const int maxLength = 600;
        return text.Length <= maxLength ? text.Trim() : text[^maxLength..].Trim();
    }

    private readonly record struct ArtifactCollection(
        IReadOnlyList<string> Directories,
        IReadOnlyList<string> Files);
}
