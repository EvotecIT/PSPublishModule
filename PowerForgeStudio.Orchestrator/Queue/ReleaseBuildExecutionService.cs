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
        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
        {
            var configPath = repository.UnifiedReleaseConfigPath!;
            var unified = _executeUnifiedReleaseBuild(configPath, CreateUnifiedReleaseBuildRequest(configPath));
            results.AddRange(CreateUnifiedAdapterResults(repository, unified, DateTimeOffset.UtcNow - startedAt));
            return ReleaseQueueExecutionResultFactory.CreateBuildResult(
                repositoryRoot,
                DateTimeOffset.UtcNow - startedAt,
                results,
                JsonSerializer.Serialize(unified));
        }

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            results.Add(await ExecuteProjectBuildAsync(repository, cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            results.Add(await ExecuteModuleBuildAsync(repository, cancellationToken));
        }

        return ReleaseQueueExecutionResultFactory.CreateBuildResult(
            repositoryRoot,
            DateTimeOffset.UtcNow - startedAt,
            results);
    }

    internal static PowerForgeReleaseRequest CreateUnifiedReleaseBuildRequest(string configPath)
        => new() {
            ConfigPath = configPath,
            PublishNuget = false,
            PublishProjectGitHub = false,
            PublishToolGitHub = false,
            ModuleRunMode = ConfigurationGateMode.Build,
            ModuleNoSign = true,
            ModuleSkipInstall = true,
            EnableSigning = false,
            SkipAppleApps = true,
            SubmitWinget = false
        };

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
            var artifacts = !string.IsNullOrWhiteSpace(buildInput)
                ? CollectModuleArtifacts(repository.RootPath, buildInput!)
                : CollectExplicitArtifacts(unified.ModuleAssets);
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
        var execution = await _moduleBuildHostService.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = repository.RootPath,
            ConfigPath = configBacked ? buildInputPath : null,
            ScriptPath = configBacked ? null : buildInputPath,
            ModulePath = modulePath,
            Framework = configBacked ? "auto" : null,
            RunMode = configBacked ? ConfigurationGateMode.Build : null,
            IncludeProjectPackages = string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath),
            SkipInstall = configBacked,
            NoSign = configBacked
        }, cancellationToken);
        var artifactInfo = CollectModuleArtifacts(repository.RootPath, buildInputPath);
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
        CollectArtifactFiles(directories, files);
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

    private static ArtifactCollection CollectModuleArtifacts(string repositoryRoot, string buildInputPath)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateDirectories = string.Equals(Path.GetExtension(buildInputPath), ".json", StringComparison.OrdinalIgnoreCase) &&
                                   new ModulePipelineConfigurationService().TryLoad(buildInputPath, out var context)
            ? context!.ArtifactPaths
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

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

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

    private static void CollectArtifactFiles(IEnumerable<string> directories, ISet<string> files)
    {
        foreach (var directory in directories)
        {
            foreach (var extension in new[] { "*.nupkg", "*.snupkg", "*.zip", "*.psd1", "*.psm1", "*.dll" })
            {
                foreach (var file in Directory.EnumerateFiles(directory, extension, SearchOption.AllDirectories).Take(50))
                {
                    files.Add(file);
                }
            }
        }
    }

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
