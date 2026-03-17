using PowerForge;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPlanPreviewService
{
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ProjectBuildCommandHostService _projectBuildCommandHostService;
    private readonly ModuleBuildHostService _moduleBuildHostService;

    public RepositoryPlanPreviewService()
        : this(new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ModuleBuildHostService())
    {
    }

    internal RepositoryPlanPreviewService(
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ModuleBuildHostService moduleBuildHostService)
    {
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _moduleBuildHostService = moduleBuildHostService;
    }

    public async Task<IReadOnlyList<RepositoryPortfolioItem>> PopulatePlanPreviewAsync(
        IEnumerable<RepositoryPortfolioItem> items,
        PlanPreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PlanPreviewOptions();
        var materialized = items.ToList();
        var targetCount = options.MaxRepositories < 0
            ? materialized.Count
            : Math.Max(0, options.MaxRepositories);
        var planTargets = materialized
            .OrderBy(item => GetPreviewPriority(item.Repository.RepositoryKind))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(targetCount)
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updated = new List<RepositoryPortfolioItem>(materialized.Count);
        for (var index = 0; index < materialized.Count; index++)
        {
            var item = materialized[index];
            if (!planTargets.Contains(item.RootPath))
            {
                updated.Add(item with {
                    PlanResults = []
                });
                continue;
            }

            var results = new List<RepositoryPlanResult>();
            if (!string.IsNullOrWhiteSpace(item.Repository.ModuleBuildScriptPath))
            {
                results.Add(await RunModulePlanAsync(item, cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(item.Repository.ProjectBuildScriptPath))
            {
                results.Add(await RunProjectPlanAsync(item, cancellationToken));
            }

            updated.Add(item with {
                PlanResults = results
            });
        }

        return updated;
    }

    private static int GetPreviewPriority(Domain.Catalog.ReleaseRepositoryKind repositoryKind)
        => repositoryKind switch
        {
            Domain.Catalog.ReleaseRepositoryKind.Mixed => 0,
            Domain.Catalog.ReleaseRepositoryKind.Library => 1,
            Domain.Catalog.ReleaseRepositoryKind.Module => 2,
            _ => 3
        };

    private async Task<RepositoryPlanResult> RunModulePlanAsync(RepositoryPortfolioItem item, CancellationToken cancellationToken)
    {
        var modulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath();
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ModuleJsonExport, "powerforge.json");
        var execution = await _moduleBuildHostService.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
            RepositoryRoot = item.Repository.RootPath,
            ScriptPath = item.Repository.ModuleBuildScriptPath!,
            ModulePath = modulePath,
            OutputPath = outputPath
        }, cancellationToken);
        var success = execution.Succeeded && File.Exists(outputPath);

        return new RepositoryPlanResult(
            AdapterKind: RepositoryPlanAdapterKind.ModuleJsonExport,
            Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
            Summary: success ? "Module JSON config exported." : "Module JSON export failed.",
            PlanPath: success ? outputPath : null,
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private async Task<RepositoryPlanResult> RunProjectPlanAsync(RepositoryPortfolioItem item, CancellationToken cancellationToken)
    {
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ProjectPlan, "project.plan.json");
        var configPath = ResolveProjectConfigPath(item.Repository.ProjectBuildScriptPath!, item.Repository.RootPath);
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var execution = _projectBuildHostService.Execute(new ProjectBuildHostRequest {
                ConfigPath = configPath,
                PlanOutputPath = outputPath,
                ExecuteBuild = false,
                PlanOnly = true,
                UpdateVersions = false,
                Build = false,
                PublishNuget = false,
                PublishGitHub = false
            });

            var success = execution.Success && File.Exists(outputPath);
            return new RepositoryPlanResult(
                AdapterKind: RepositoryPlanAdapterKind.ProjectPlan,
                Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
                Summary: success ? "Project build plan generated." : "Project build plan failed.",
                PlanPath: success ? outputPath : null,
                ExitCode: success ? 0 : 1,
                DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
                OutputTail: null,
                ErrorTail: success ? null : execution.ErrorMessage);
        }

        var powerShellExecution = await _projectBuildCommandHostService.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
            RepositoryRoot = item.Repository.RootPath,
            PlanOutputPath = outputPath,
            ConfigPath = configPath,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
        }, cancellationToken);

        return BuildResult(
            RepositoryPlanAdapterKind.ProjectPlan,
            outputPath,
            powerShellExecution,
            successSummary: "Project build plan generated.",
            failureSummary: "Project build plan failed.");
    }

    private static RepositoryPlanResult BuildResult(
        RepositoryPlanAdapterKind adapterKind,
        string outputPath,
        ProjectBuildCommandHostExecutionResult execution,
        string successSummary,
        string failureSummary)
    {
        var success = execution.ExitCode == 0 && File.Exists(outputPath);
        return new RepositoryPlanResult(
            AdapterKind: adapterKind,
            Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
            Summary: success ? successSummary : failureSummary,
            PlanPath: success ? outputPath : null,
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private static string BuildPlanOutputPath(string repositoryName, RepositoryPlanAdapterKind adapterKind, string fileName)
    {
        return PowerForgeStudioHostPaths.GetPlansFilePath(repositoryName, adapterKind.ToString(), fileName);
    }

    internal static string? ResolveProjectConfigPath(string projectBuildScriptPath, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectBuildScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var buildDirectory = Path.GetDirectoryName(projectBuildScriptPath);
        if (!string.IsNullOrWhiteSpace(buildDirectory))
        {
            var siblingConfig = Path.Combine(buildDirectory, "project.build.json");
            if (File.Exists(siblingConfig))
            {
                return siblingConfig;
            }
        }

        var rootConfig = Path.Combine(repositoryRoot, "Build", "project.build.json");
        return File.Exists(rootConfig) ? rootConfig : null;
    }

    private static string? TrimTail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const int maxLength = 600;
        return text.Length <= maxLength ? text.Trim() : text[^maxLength..].Trim();
    }
}
