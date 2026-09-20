using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPlanPreviewService : IRepositoryPlanPreviewService
{
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ProjectBuildCommandHostService _projectBuildCommandHostService;
    private readonly ModuleBuildHostService _moduleBuildHostService;
    private readonly Func<string, PowerForgeReleaseResult> _planUnifiedRelease;

    public RepositoryPlanPreviewService()
        : this(new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ModuleBuildHostService())
    {
    }

    internal RepositoryPlanPreviewService(
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ModuleBuildHostService moduleBuildHostService,
        Func<string, PowerForgeReleaseResult>? planUnifiedRelease = null)
    {
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _moduleBuildHostService = moduleBuildHostService;
        _planUnifiedRelease = planUnifiedRelease ?? PlanUnifiedRelease;
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

            var results = await PlanRepositoryAsync(item.Repository, cancellationToken).ConfigureAwait(false);

            updated.Add(item with {
                PlanResults = results
            });
        }

        return updated;
    }

    /// <summary>Plans one working copy without requiring a portfolio or GitHub snapshot.
    /// PowerShell contracts may execute project code to export configuration.</summary>
    public async Task<IReadOnlyList<RepositoryPlanResult>> PlanRepositoryAsync(
        RepositoryCatalogEntry repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RepositoryPlanResult>();
        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
            results.Add(RunUnifiedReleasePlan(repository));
        else
        {
            if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
                results.Add(await RunAdapterAsync(RepositoryPlanAdapterKind.ModuleJsonExport, () => RunModulePlanAsync(repository, cancellationToken)).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
                results.Add(await RunAdapterAsync(RepositoryPlanAdapterKind.ProjectPlan, () => RunProjectPlanAsync(repository, cancellationToken)).ConfigureAwait(false));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static async Task<RepositoryPlanResult> RunAdapterAsync(
        RepositoryPlanAdapterKind kind, Func<Task<RepositoryPlanResult>> action)
    {
        var started = DateTimeOffset.UtcNow;
        try { return await action().ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new RepositoryPlanResult(kind, RepositoryPlanStatus.Failed,
                "Plan generation failed.", null, 1, (DateTimeOffset.UtcNow - started).TotalSeconds,
                ErrorTail: TrimTail(ex.Message));
        }
    }

    private RepositoryPlanResult RunUnifiedReleasePlan(RepositoryCatalogEntry item)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var result = _planUnifiedRelease(item.UnifiedReleaseConfigPath!);
            return new RepositoryPlanResult(
                RepositoryPlanAdapterKind.UnifiedRelease,
                result.Success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
                result.Success ? "Unified release plan generated." : "Unified release plan failed.",
                result.Success ? item.UnifiedReleaseConfigPath : null,
                result.Success ? 0 : 1,
                Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 2),
                null,
                TrimTail(result.ErrorMessage ?? string.Empty));
        }
        catch (Exception ex)
        {
            return new RepositoryPlanResult(
                RepositoryPlanAdapterKind.UnifiedRelease,
                RepositoryPlanStatus.Failed,
                "Unified release plan failed.",
                null,
                1,
                Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 2),
                null,
                TrimTail(ex.Message));
        }
    }

    private static PowerForgeReleaseResult PlanUnifiedRelease(string configPath)
    {
        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        return new PowerForgeReleaseService(new NullLogger()).Execute(
            spec,
            new PowerForgeReleaseRequest {
                ConfigPath = configPath,
                PlanOnly = true,
                PublishNuget = false,
                PublishProjectGitHub = false,
                PublishToolGitHub = false
            });
    }

    private static int GetPreviewPriority(Domain.Catalog.ReleaseRepositoryKind repositoryKind)
        => repositoryKind switch
        {
            Domain.Catalog.ReleaseRepositoryKind.Mixed => 0,
            Domain.Catalog.ReleaseRepositoryKind.Library => 1,
            Domain.Catalog.ReleaseRepositoryKind.Module => 2,
            _ => 3
        };

    private async Task<RepositoryPlanResult> RunModulePlanAsync(RepositoryCatalogEntry item, CancellationToken cancellationToken)
    {
        var moduleBuildInput = item.ModuleBuildScriptPath!;
        if (string.Equals(Path.GetExtension(moduleBuildInput), ".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _ = new ModulePipelineConfigurationService().Load(moduleBuildInput);
                return new RepositoryPlanResult(
                    AdapterKind: RepositoryPlanAdapterKind.ModuleJsonExport,
                    Status: RepositoryPlanStatus.Succeeded,
                    Summary: "Module JSON configuration validated; no build plan generated.",
                    PlanPath: moduleBuildInput,
                    ExitCode: 0,
                    DurationSeconds: 0,
                    OutputTail: null,
                    ErrorTail: null);
            }
            catch (Exception ex)
            {
                return new RepositoryPlanResult(
                    AdapterKind: RepositoryPlanAdapterKind.ModuleJsonExport,
                    Status: RepositoryPlanStatus.Failed,
                    Summary: "Module JSON config validation failed.",
                    PlanPath: null,
                    ExitCode: 1,
                    DurationSeconds: 0,
                    OutputTail: null,
                    ErrorTail: TrimTail(ex.Message));
            }
        }

        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ModuleJsonExport, "powerforge.json");
        var execution = await _moduleBuildHostService.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
            RepositoryRoot = item.RootPath,
            ScriptPath = moduleBuildInput,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
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

    private async Task<RepositoryPlanResult> RunProjectPlanAsync(RepositoryCatalogEntry item, CancellationToken cancellationToken)
    {
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ProjectPlan, "project.plan.json");
        var configPath = ResolveProjectConfigPath(item.ProjectBuildScriptPath!, item.RootPath);
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
            RepositoryRoot = item.RootPath,
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

        if (string.Equals(Path.GetExtension(projectBuildScriptPath), ".json", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(projectBuildScriptPath))
        {
            return projectBuildScriptPath;
        }

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
