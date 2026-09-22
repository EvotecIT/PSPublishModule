using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPlanPreviewService : IRepositoryPlanPreviewService
{
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ProjectBuildCommandHostService _projectBuildCommandHostService;
    private readonly ModuleBuildHostService _moduleBuildHostService;
    private readonly ModuleBuildPlanService _moduleBuildPlanService;
    private readonly Func<string, PowerForgeReleaseRequest, PowerForgeReleaseResult> _planUnifiedRelease;

    public RepositoryPlanPreviewService()
        : this(new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ModuleBuildHostService())
    {
    }

    internal RepositoryPlanPreviewService(
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ModuleBuildHostService moduleBuildHostService,
        Func<string, PowerForgeReleaseRequest, PowerForgeReleaseResult>? planUnifiedRelease = null)
    {
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _moduleBuildHostService = moduleBuildHostService;
        _moduleBuildPlanService = new ModuleBuildPlanService(new NullLogger());
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
            results.Add(await RunAdapterAsync(
                RepositoryPlanAdapterKind.UnifiedRelease,
                () => RunUnifiedReleasePlanAsync(repository, cancellationToken)).ConfigureAwait(false));
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

    private async Task<RepositoryPlanResult> RunUnifiedReleasePlanAsync(
        RepositoryCatalogEntry item,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var configPath = item.UnifiedReleaseConfigPath!;
        var stagingPath = Path.GetDirectoryName(BuildPlanOutputPath(
            item.Name,
            RepositoryPlanAdapterKind.UnifiedRelease,
            "module-staging.marker"))!;
        var request = ReleaseBuildExecutionService.CreateUnifiedReleaseBuildRequest(
            configPath,
            PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
            stagingPath);
        request.PlanOnly = true;
        request.CancellationToken = cancellationToken;
        var result = _planUnifiedRelease(configPath, request);
        var modulePipelinePlan = result.Success
            ? await ResolveUnifiedModulePlanAsync(item, result, cancellationToken).ConfigureAwait(false)
            : null;
        return new RepositoryPlanResult(
            RepositoryPlanAdapterKind.UnifiedRelease,
            result.Success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
            result.Success ? "Unified release plan generated." : "Unified release plan failed.",
            result.Success ? configPath : null,
            result.Success ? 0 : 1,
            Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 2),
            null,
            TrimTail(result.ErrorMessage ?? string.Empty))
        {
            Actions = result.Success
                ? RepositoryPlanActionProjectionService.FromUnified(result, modulePipelinePlan)
                : []
        };
    }

    private static PowerForgeReleaseResult PlanUnifiedRelease(
        string configPath,
        PowerForgeReleaseRequest request)
    {
        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        return new PowerForgeReleaseService(new NullLogger()).Execute(spec, request);
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
                var request = ReleaseBuildExecutionService.CreateModuleBuildRequest(
                    item,
                    BuildModulePlanStagingPath(item.Name));
                var plan = _moduleBuildPlanService.Plan(request);
                return new RepositoryPlanResult(
                    AdapterKind: RepositoryPlanAdapterKind.ModuleJsonExport,
                    Status: RepositoryPlanStatus.Succeeded,
                    Summary: "Module JSON configuration validated and reviewed plan generated.",
                    PlanPath: moduleBuildInput,
                    ExitCode: 0,
                    DurationSeconds: 0,
                    OutputTail: null,
                    ErrorTail: null)
                {
                    Actions = RepositoryPlanActionProjectionService.FromModule(plan)
                };
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

        var actions = success
            ? RepositoryPlanActionProjectionService.FromModule(_moduleBuildPlanService.Plan(
                ReleaseBuildExecutionService.CreateModuleBuildRequest(
                    item with { ModuleBuildScriptPath = outputPath },
                    BuildModulePlanStagingPath(item.Name))))
            : [];
        return new RepositoryPlanResult(
            AdapterKind: RepositoryPlanAdapterKind.ModuleJsonExport,
            Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
            Summary: success ? "Module JSON config exported." : "Module JSON export failed.",
            PlanPath: success ? outputPath : null,
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError))
        {
            Actions = actions
        };
    }

    private async Task<RepositoryPlanResult> RunProjectPlanAsync(RepositoryCatalogEntry item, CancellationToken cancellationToken)
    {
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ProjectPlan, "project.plan.json");
        if (File.Exists(outputPath)) File.Delete(outputPath);
        var configPath = ResolveProjectConfigPath(item.ProjectBuildScriptPath!, item.RootPath);
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var execution = _projectBuildHostService.Execute(
                ReleaseBuildExecutionService.CreateProjectBuildRequest(
                    configPath,
                    executeBuild: false,
                    planOutputPath: outputPath,
                    cancellationToken: cancellationToken));

            var success = execution.Success && File.Exists(outputPath);
            return new RepositoryPlanResult(
                AdapterKind: RepositoryPlanAdapterKind.ProjectPlan,
                Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
                Summary: success ? "Project build plan generated." : "Project build plan failed.",
                PlanPath: success ? outputPath : null,
                ExitCode: success ? 0 : 1,
                DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
                OutputTail: null,
                ErrorTail: success ? null : execution.ErrorMessage)
            {
                Actions = success ? RepositoryPlanActionProjectionService.FromProject(execution) : []
            };
        }

        var powerShellExecution = await _projectBuildCommandHostService.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
            RepositoryRoot = item.RootPath,
            PlanOutputPath = outputPath,
            ConfigPath = configPath,
            ScriptPath = item.ProjectBuildScriptPath,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
        }, cancellationToken);

        return BuildResult(
            RepositoryPlanAdapterKind.ProjectPlan,
            outputPath,
            powerShellExecution,
            successSummary: "Project build plan generated.",
            failureSummary: "Project build plan failed.");
    }

    private async Task<ModulePipelinePlan?> ResolveUnifiedModulePlanAsync(
        RepositoryCatalogEntry item,
        PowerForgeReleaseResult result,
        CancellationToken cancellationToken)
    {
        var summary = result.ModulePlan;
        if (summary is null) return null;

        var repositoryRoot = string.IsNullOrWhiteSpace(summary.RepositoryRoot)
            ? item.RootPath
            : summary.RepositoryRoot;
        var configPath = ResolveOptionalPath(repositoryRoot, summary.ConfigPath);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var scriptPath = ResolveOptionalPath(
                repositoryRoot,
                summary.ScriptPath ?? item.ModuleBuildScriptPath);
            if (string.IsNullOrWhiteSpace(scriptPath))
                throw new InvalidDataException("The unified module lane did not identify a JSON config or exportable PowerShell script.");

            configPath = BuildPlanOutputPath(
                item.Name,
                RepositoryPlanAdapterKind.UnifiedRelease,
                "module.powerforge.json");
            var export = await _moduleBuildHostService.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
                RepositoryRoot = repositoryRoot,
                ScriptPath = scriptPath,
                ModulePath = string.IsNullOrWhiteSpace(summary.ModulePath)
                    ? PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
                    : summary.ModulePath,
                OutputPath = configPath
            }, cancellationToken).ConfigureAwait(false);
            if (!export.Succeeded || !File.Exists(configPath))
                throw new InvalidDataException(
                    $"The unified module script could not export reviewable JSON. {TrimTail(export.StandardError)}");
        }

        var request = new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = repositoryRoot,
            ConfigPath = configPath,
            ModulePath = string.IsNullOrWhiteSpace(summary.ModulePath)
                ? PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
                : summary.ModulePath,
            Configuration = summary.Configuration,
            Framework = summary.Framework,
            RunMode = summary.RunMode,
            PowerForgeReleaseStage = summary.PowerForgeReleaseStage,
            UnifiedGitHubRelease = summary.UnifiedGitHubRelease,
            NoDotnetBuild = summary.NoDotnetBuild,
            NoDotnetBuildWasSpecified = summary.NoDotnetBuildWasSpecified,
            ModuleVersion = summary.ModuleVersion,
            PreReleaseTag = summary.PreReleaseTag,
            StagingPath = summary.StagingPath,
            NoSign = summary.NoSign,
            SkipInstall = summary.SkipInstall,
            SignModule = summary.SignModule,
            SignModuleWasSpecified = summary.SignModuleWasSpecified,
            IncludeProjectPackages = summary.IncludesProjectPackages,
            IncludeModulePublishing = summary.IncludeModulePublishing,
            Timeout = summary.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(summary.TimeoutSeconds)
                : TimeSpan.FromHours(2)
        };
        return _moduleBuildPlanService.Plan(request);
    }

    private static string BuildModulePlanStagingPath(string repositoryName)
        => Path.GetDirectoryName(BuildPlanOutputPath(
            repositoryName,
            RepositoryPlanAdapterKind.ModuleJsonExport,
            "module-staging.marker"))!;

    private static string? ResolveOptionalPath(string repositoryRoot, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(repositoryRoot, value));

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
            ErrorTail: TrimTail(execution.StandardError))
        {
            Actions = success ? RepositoryPlanActionProjectionService.FromProjectPlanFile(outputPath) : []
        };
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
