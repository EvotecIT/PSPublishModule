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

    public ReleaseBuildExecutionService()
        : this(new RepositoryCatalogScanner(), new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ModuleBuildHostService())
    {
    }

    internal ReleaseBuildExecutionService(
        RepositoryCatalogScanner catalogScanner,
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ModuleBuildHostService moduleBuildHostService)
    {
        _catalogScanner = catalogScanner;
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _moduleBuildHostService = moduleBuildHostService;
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
        var scriptPath = repository.ModuleBuildScriptPath!;
        var modulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath();
        var execution = await _moduleBuildHostService.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = repository.RootPath,
            ScriptPath = scriptPath,
            ModulePath = modulePath
        }, cancellationToken);
        var artifactInfo = CollectModuleArtifacts(scriptPath);
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

    private static ArtifactCollection CollectModuleArtifacts(string moduleBuildScriptPath)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleRoot = Directory.GetParent(Path.GetDirectoryName(moduleBuildScriptPath)!)?.FullName;

        if (!string.IsNullOrWhiteSpace(moduleRoot))
        {
            var unpacked = Path.Combine(moduleRoot, "Artefacts", "Unpacked");
            var packed = Path.Combine(moduleRoot, "Artefacts", "Packed");

            if (Directory.Exists(unpacked))
            {
                directories.Add(unpacked);
            }

            if (Directory.Exists(packed))
            {
                directories.Add(packed);
            }
        }

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
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
