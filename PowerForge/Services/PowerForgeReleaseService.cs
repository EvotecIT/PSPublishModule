using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Orchestrates package and tool release workflows from one unified configuration.
/// </summary>
internal sealed class PowerForgeReleaseService
{
    private static readonly JsonSerializerOptions DotNetToolsJsonOptions = CreateJsonOptions();
    private static readonly JsonSerializerOptions WorkspaceValidationJsonOptions = CreateJsonOptions();

    private const string DefaultDotNetTargetOutputTemplate =
        "Artifacts/DotNetPublish/{target}/{rid}/{framework}/{style}";

    private const string DefaultDotNetBundleOutputTemplate =
        "Artifacts/DotNetPublish/Bundles/{bundle}/{rid}/{framework}/{style}";

    private const string DefaultDotNetManifestJsonTemplate =
        "Artifacts/DotNetPublish/manifest.json";

    private const string DefaultDotNetManifestTextTemplate =
        "Artifacts/DotNetPublish/manifest.txt";

    private const string DefaultDotNetChecksumsTemplate =
        "Artifacts/DotNetPublish/SHA256SUMS.txt";

    private const string DefaultDotNetRunReportTemplate =
        "Artifacts/DotNetPublish/run-report.json";

    private const string DefaultMsiPrepareStagingPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/payload";

    private const string DefaultMsiPrepareManifestPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/prepare.manifest.json";

    private const string DefaultMsiHarvestPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/harvest.wxs";

    private const string DefaultStorePackageOutputTemplate =
        "Artifacts/DotNetPublish/Store/{storePackage}/{target}/{rid}/{framework}/{style}";

    private readonly ILogger _logger;
    private readonly Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> _executePackages;
    private readonly Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> _planTools;
    private readonly Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> _runTools;
    private readonly Func<PowerForgeToolReleaseSpec, string, (DotNetPublishSpec Spec, string SourceConfigPath)> _loadDotNetToolsSpec;
    private readonly Func<DotNetPublishSpec, string, PowerForgeReleaseRequest, ISet<PowerForgeReleaseToolOutputKind>, DotNetPublishPlan> _planDotNetTools;
    private readonly Func<DotNetPublishPlan, DotNetPublishResult> _runDotNetTools;
    private readonly Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> _publishGitHubRelease;

    /// <summary>
    /// Creates a new unified release service.
    /// </summary>
    public PowerForgeReleaseService(ILogger logger)
        : this(
            logger,
            (request, config, configPath) => new ProjectBuildHostService(logger).Execute(request, config, configPath),
            (spec, configPath, request) => new PowerForgeToolReleaseService(logger).Plan(spec, configPath, request),
            plan => new PowerForgeToolReleaseService(logger).Run(plan),
            LoadDotNetToolsSpec,
            (spec, configPath, request, selectedOutputs) => PlanDotNetTools(logger, spec, configPath, request, selectedOutputs),
            plan => new DotNetPublishPipelineRunner(logger).Run(plan, progress: null),
            publishRequest => new GitHubReleasePublisher(logger).PublishRelease(publishRequest))
    {
    }

    internal PowerForgeReleaseService(
        ILogger logger,
        Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> executePackages,
        Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools,
        Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> runTools,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> publishGitHubRelease)
        : this(
            logger,
            executePackages,
            planTools,
            runTools,
            LoadDotNetToolsSpec,
            (spec, configPath, request, selectedOutputs) => PlanDotNetTools(logger, spec, configPath, request, selectedOutputs),
            plan => new DotNetPublishPipelineRunner(logger).Run(plan, progress: null),
            publishGitHubRelease)
    {
    }

    internal PowerForgeReleaseService(
        ILogger logger,
        Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> executePackages,
        Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools,
        Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> runTools,
        Func<PowerForgeToolReleaseSpec, string, (DotNetPublishSpec Spec, string SourceConfigPath)> loadDotNetToolsSpec,
        Func<DotNetPublishSpec, string, PowerForgeReleaseRequest, ISet<PowerForgeReleaseToolOutputKind>, DotNetPublishPlan> planDotNetTools,
        Func<DotNetPublishPlan, DotNetPublishResult> runDotNetTools,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> publishGitHubRelease)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executePackages = executePackages ?? throw new ArgumentNullException(nameof(executePackages));
        _planTools = planTools ?? throw new ArgumentNullException(nameof(planTools));
        _runTools = runTools ?? throw new ArgumentNullException(nameof(runTools));
        _loadDotNetToolsSpec = loadDotNetToolsSpec ?? throw new ArgumentNullException(nameof(loadDotNetToolsSpec));
        _planDotNetTools = planDotNetTools ?? throw new ArgumentNullException(nameof(planDotNetTools));
        _runDotNetTools = runDotNetTools ?? throw new ArgumentNullException(nameof(runDotNetTools));
        _publishGitHubRelease = publishGitHubRelease ?? throw new ArgumentNullException(nameof(publishGitHubRelease));
    }

    /// <summary>
    /// Executes the unified release workflow.
    /// </summary>
    public PowerForgeReleaseResult Execute(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required.", nameof(request));

        var configPath = Path.GetFullPath(request.ConfigPath.Trim().Trim('"'));
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var selectedToolOutputs = ResolveSelectedToolOutputs(request);
        var runModule = spec.Module is not null && (!request.PackagesOnly && !request.ToolsOnly || request.ModuleOnly);
        var runPackages = spec.Packages is not null && !request.ModuleOnly && !request.ToolsOnly;
        var runTools = spec.Tools is not null && !request.ModuleOnly && !request.PackagesOnly;
        var configurationOverride = NormalizeConfiguration(request.Configuration);
        var runWorkspaceValidation = spec.WorkspaceValidation is not null && !request.SkipWorkspaceValidation;
        var publishUnifiedGitHub = ShouldPublishUnifiedGitHub(spec, request);

        if (!runModule && !runPackages && !runTools && !runWorkspaceValidation)
        {
            return new PowerForgeReleaseResult
            {
                Success = false,
                ConfigPath = configPath,
                ErrorMessage = "Release config does not enable any selected WorkspaceValidation, Module, Packages, or Tools sections."
            };
        }

        var result = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = configPath
        };

        if (runWorkspaceValidation)
        {
            var workspace = PrepareWorkspaceValidation(spec.WorkspaceValidation!, configPath, request, configurationOverride);
            result.WorkspaceValidationPlan = workspace.Plan;

            if (!request.PlanOnly && !request.ValidateOnly)
            {
                var workspaceResult = workspace.Service.RunAsync(workspace.Spec, workspace.ConfigPath, workspace.Request).GetAwaiter().GetResult();
                result.WorkspaceValidation = workspaceResult;
                if (!workspaceResult.Succeeded)
                {
                    result.Success = false;
                    result.ErrorMessage = workspaceResult.ErrorMessage ?? "Workspace validation failed.";
                    return result;
                }
            }
        }

        if (runModule)
        {
            var module = PrepareModuleRelease(spec.Module!, configPath, request, configurationOverride);
            result.ModulePlan = module.Plan;
            result.ModuleAssets = module.ArtifactPaths;

            if (!request.PlanOnly && !request.ValidateOnly)
            {
                var moduleResult = new ModuleBuildHostService().ExecuteBuildAsync(module.Request).GetAwaiter().GetResult();
                result.Module = moduleResult;
                if (!moduleResult.Succeeded)
                {
                    result.Success = false;
                    result.ErrorMessage = BuildModuleFailureMessage(module.Request.ScriptPath, moduleResult);
                    return result;
                }
            }
        }

        if (runPackages)
        {
            ApplyPackageRequestOverrides(spec.Packages!, request, configurationOverride);
            var packageRequest = new ProjectBuildHostRequest
            {
                ConfigPath = configPath,
                ExecuteBuild = !request.PlanOnly && !request.ValidateOnly,
                PlanOnly = request.PlanOnly || request.ValidateOnly ? true : null,
                PublishNuget = request.PublishNuget,
                PublishGitHub = publishUnifiedGitHub ? false : request.PublishProjectGitHub
            };

            var packages = _executePackages(packageRequest, spec.Packages!, configPath);
            result.Packages = packages;
            if (!packages.Success)
            {
                result.Success = false;
                result.ErrorMessage = packages.ErrorMessage ?? "Package release workflow failed.";
                return result;
            }
        }

        var sharedReleaseVersion = ResolveSharedReleaseVersion(spec, result);

        if (runTools)
        {
            ApplyToolRequestOverrides(spec.Tools!, request, configurationOverride);
            if (UsesDotNetToolWorkflow(spec.Tools!))
            {
                var (dotNetSpec, dotNetSourcePath) = _loadDotNetToolsSpec(spec.Tools!, configPath);
                var dotNetPlan = _planDotNetTools(dotNetSpec, dotNetSourcePath, request, selectedToolOutputs);
                ApplySharedReleaseVersion(dotNetPlan, sharedReleaseVersion);
                ApplyDotNetPublishSkipFlags(dotNetPlan, request.SkipRestore, request.SkipBuild);
                result.DotNetToolPlan = dotNetPlan;

                if (!request.PlanOnly && !request.ValidateOnly)
                {
                    var dotNetTools = _runDotNetTools(dotNetPlan);
                    FilterDotNetToolResult(dotNetTools, selectedToolOutputs);
                    result.DotNetTools = dotNetTools;
                    if (!dotNetTools.Succeeded)
                    {
                        result.Success = false;
                        result.ErrorMessage = dotNetTools.ErrorMessage ?? "DotNet tool release workflow failed.";
                        return result;
                    }

                    var publishToolGitHub = request.PublishToolGitHub ?? spec.Tools!.GitHub.Publish;
                    if (publishToolGitHub)
                    {
                        var releases = PublishDotNetToolGitHubReleases(spec, configDirectory, dotNetPlan, dotNetTools, sharedReleaseVersion);
                        result.ToolGitHubReleases = releases;
                        var failures = releases.Where(entry => !entry.Success).ToArray();
                        if (failures.Length > 0)
                        {
                            result.Success = false;
                            result.ErrorMessage = failures[0].ErrorMessage ?? "Tool GitHub release publishing failed.";
                            return result;
                        }
                    }
                }
            }
            else
            {
                var toolPlan = _planTools(spec.Tools!, configPath, request);
                result.ToolPlan = toolPlan;

                if (!request.PlanOnly && !request.ValidateOnly)
                {
                    var tools = _runTools(toolPlan);
                    result.Tools = tools;
                    if (!tools.Success)
                    {
                        result.Success = false;
                        result.ErrorMessage = tools.ErrorMessage ?? "Tool release workflow failed.";
                        return result;
                    }

                    var publishToolGitHub = request.PublishToolGitHub ?? spec.Tools!.GitHub.Publish;
                    if (publishToolGitHub)
                    {
                        var releases = PublishLegacyToolGitHubReleases(spec, configDirectory, tools);
                        result.ToolGitHubReleases = releases;
                        var failures = releases.Where(entry => !entry.Success).ToArray();
                        if (failures.Length > 0)
                        {
                            result.Success = false;
                            result.ErrorMessage = failures[0].ErrorMessage ?? "Tool GitHub release publishing failed.";
                            return result;
                        }
                    }
                }
            }
        }

        if (!request.PlanOnly && !request.ValidateOnly)
        {
            PopulateReleaseOutputs(spec, request, configDirectory, result, sharedReleaseVersion);
            GenerateWingetOutputs(spec, request, configDirectory, result);
            IncludeWingetOutputsInReleaseAssets(result);
            if (publishUnifiedGitHub)
            {
                var unifiedGitHubRelease = PublishUnifiedGitHubRelease(spec, configDirectory, result, sharedReleaseVersion);
                result.UnifiedGitHubRelease = unifiedGitHubRelease;
                if (!unifiedGitHubRelease.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = unifiedGitHubRelease.ErrorMessage ?? "Unified GitHub release publishing failed.";
                    return result;
                }
            }
            RewriteReleaseSummaryFiles(result);
        }

        return result;
    }

    private static (WorkspaceValidationService Service, WorkspaceValidationSpec Spec, string ConfigPath, WorkspaceValidationRequest Request, WorkspaceValidationPlan Plan) PrepareWorkspaceValidation(
        PowerForgeWorkspaceValidationOptions options,
        string releaseConfigPath,
        PowerForgeReleaseRequest request,
        string? configurationOverride)
    {
        var (spec, workspaceConfigPath) = LoadWorkspaceValidationSpec(options, releaseConfigPath, request);
        var service = new WorkspaceValidationService();
        var workspaceRequest = new WorkspaceValidationRequest
        {
            ProfileName = request.WorkspaceProfile ?? options.Profile ?? "default",
            Configuration = configurationOverride ?? "Release",
            TestimoXRoot = request.WorkspaceTestimoXRoot,
            EnabledFeatures = (request.WorkspaceEnableFeatures?.Length > 0 ? request.WorkspaceEnableFeatures : options.EnableFeatures) ?? Array.Empty<string>(),
            DisabledFeatures = (request.WorkspaceDisableFeatures?.Length > 0 ? request.WorkspaceDisableFeatures : options.DisableFeatures) ?? Array.Empty<string>(),
            CaptureOutput = false,
            CaptureError = false
        };

        var plan = service.Plan(spec, workspaceConfigPath, workspaceRequest);
        return (service, spec, workspaceConfigPath, workspaceRequest, plan);
    }

    private static (ModuleBuildHostBuildRequest Request, PowerForgeModuleReleasePlanSummary Plan, string[] ArtifactPaths) PrepareModuleRelease(
        PowerForgeModuleReleaseOptions options,
        string releaseConfigPath,
        PowerForgeReleaseRequest request,
        string? configurationOverride)
    {
        var configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = Path.GetFullPath(Path.IsPathRooted(options.RepositoryRoot)
            ? options.RepositoryRoot!
            : Path.Combine(configDirectory, string.IsNullOrWhiteSpace(options.RepositoryRoot) ? "." : options.RepositoryRoot!));
        var scriptPath = Path.GetFullPath(Path.IsPathRooted(options.ScriptPath)
            ? options.ScriptPath!
            : Path.Combine(repositoryRoot, string.IsNullOrWhiteSpace(options.ScriptPath) ? Path.Combine("Module", "Build", "Build-Module.ps1") : options.ScriptPath!));
        var modulePath = string.IsNullOrWhiteSpace(options.ModulePath)
            ? "PSPublishModule"
            : Path.IsPathRooted(options.ModulePath)
                ? options.ModulePath!
                : Path.Combine(repositoryRoot, options.ModulePath!);

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Module build script was not found: {scriptPath}", scriptPath);

        var artifactPaths = (options.ArtifactPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(repositoryRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var buildRequest = new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = repositoryRoot,
            ScriptPath = scriptPath,
            ModulePath = modulePath,
            Configuration = configurationOverride,
            NoDotnetBuild = request.ModuleNoDotnetBuild ?? options.NoDotnetBuild ?? false,
            ModuleVersion = request.ModuleVersion ?? options.ModuleVersion,
            PreReleaseTag = request.ModulePreReleaseTag ?? options.PreReleaseTag,
            NoSign = request.ModuleNoSign ?? options.NoSign ?? false,
            SignModule = request.ModuleSignModule ?? options.SignModule ?? false
        };

        var plan = new PowerForgeModuleReleasePlanSummary
        {
            RepositoryRoot = repositoryRoot,
            ScriptPath = scriptPath,
            ModulePath = modulePath,
            Configuration = buildRequest.Configuration,
            NoDotnetBuild = buildRequest.NoDotnetBuild,
            ModuleVersion = buildRequest.ModuleVersion,
            PreReleaseTag = buildRequest.PreReleaseTag,
            NoSign = buildRequest.NoSign,
            SignModule = buildRequest.SignModule,
            ArtifactPaths = artifactPaths
        };

        return (buildRequest, plan, artifactPaths);
    }

    private static (WorkspaceValidationSpec Spec, string ConfigPath) LoadWorkspaceValidationSpec(
        PowerForgeWorkspaceValidationOptions options,
        string releaseConfigPath,
        PowerForgeReleaseRequest request)
    {
        var configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var configuredPath = request.WorkspaceConfigPath ?? options.ConfigPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("WorkspaceValidation.ConfigPath is required when the unified release workflow enables workspace validation.");

        var fullPath = ResolveOutputPath(configDirectory, configuredPath!);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Workspace validation config not found: {fullPath}", fullPath);

        var json = File.ReadAllText(fullPath);
        var spec = JsonSerializer.Deserialize<WorkspaceValidationSpec>(json, WorkspaceValidationJsonOptions);
        if (spec is null)
            throw new InvalidOperationException($"Unable to deserialize workspace validation config: {fullPath}");

        return (spec, fullPath);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string? ResolveSharedReleaseVersion(PowerForgeReleaseSpec spec, PowerForgeReleaseResult result)
    {
        var release = result.Packages?.Result.Release;
        if (release is null)
            return null;

        var primaryProject = string.IsNullOrWhiteSpace(spec.Packages?.GitHubPrimaryProject)
            ? null
            : spec.Packages!.GitHubPrimaryProject!.Trim();
        if (!string.IsNullOrWhiteSpace(primaryProject))
        {
            if (release.ResolvedVersionsByProject.TryGetValue(primaryProject!, out var primaryVersion)
                && !string.IsNullOrWhiteSpace(primaryVersion))
            {
                return primaryVersion;
            }
        }

        if (!string.IsNullOrWhiteSpace(release.ResolvedVersion))
            return release.ResolvedVersion;

        var distinctVersions = release.ResolvedVersionsByProject.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinctVersions.Length == 1 ? distinctVersions[0] : null;
    }

    private static void ApplySharedReleaseVersion(DotNetPublishPlan plan, string? sharedReleaseVersion)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return;

        foreach (var entry in BuildSharedReleaseVersionProperties(sharedReleaseVersion!))
            plan.MsBuildProperties[entry.Key] = entry.Value;
    }

    private static Dictionary<string, string> BuildSharedReleaseVersionProperties(string sharedReleaseVersion)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Version"] = sharedReleaseVersion,
            ["PackageVersion"] = sharedReleaseVersion,
            ["InformationalVersion"] = sharedReleaseVersion
        };

        var numericVersion = NormalizeSharedReleaseAssemblyVersion(sharedReleaseVersion);
        if (!string.IsNullOrWhiteSpace(numericVersion))
        {
            properties["VersionPrefix"] = numericVersion!;
            properties["AssemblyVersion"] = numericVersion!;
            properties["FileVersion"] = numericVersion!;
        }

        return properties;
    }

    private static string? NormalizeSharedReleaseAssemblyVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var core = version.Trim();
        var separatorIndex = core.IndexOfAny(new[] { '-', '+' });
        if (separatorIndex >= 0)
            core = core.Substring(0, separatorIndex);

        if (string.IsNullOrWhiteSpace(core))
            return null;

        var segments = core.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();
        if (segments.Length == 0 || segments.Length > 4)
            return null;

        if (segments.Any(segment => !int.TryParse(segment, out _)))
            return null;

        return string.Join(".", segments);
    }

    private void PopulateReleaseOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
    {
        var assetEntries = CollectReleaseAssetEntries(result, result.DotNetToolPlan, sharedReleaseVersion)
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var outputs = new PowerForgeReleaseOutputsOptions
        {
            ManifestJsonPath = request.ManifestJsonPath ?? spec.Outputs?.ManifestJsonPath,
            ChecksumsPath = request.SkipReleaseChecksums ? null : request.ChecksumsPath ?? spec.Outputs?.ChecksumsPath,
            Staging = spec.Outputs?.Staging
        };
        var stageRootTemplate = request.StageRoot ?? outputs.Staging?.RootPath;
        if (!string.IsNullOrWhiteSpace(request.StageRoot))
        {
            if (string.IsNullOrWhiteSpace(request.ManifestJsonPath))
                outputs.ManifestJsonPath = Path.Combine(request.StageRoot!, "release-manifest.json");
            if (!request.SkipReleaseChecksums && string.IsNullOrWhiteSpace(request.ChecksumsPath))
                outputs.ChecksumsPath = Path.Combine(request.StageRoot!, "SHA256SUMS.txt");
        }
        else if (!string.IsNullOrWhiteSpace(stageRootTemplate))
        {
            if (string.IsNullOrWhiteSpace(outputs.ManifestJsonPath))
                outputs.ManifestJsonPath = Path.Combine(stageRootTemplate!, "release-manifest.json");
            if (!request.SkipReleaseChecksums && string.IsNullOrWhiteSpace(outputs.ChecksumsPath))
                outputs.ChecksumsPath = Path.Combine(stageRootTemplate!, "SHA256SUMS.txt");
        }

        if (string.IsNullOrWhiteSpace(outputs.ManifestJsonPath)
            && string.IsNullOrWhiteSpace(outputs.ChecksumsPath)
            && string.IsNullOrWhiteSpace(stageRootTemplate))
        {
            result.ReleaseAssetEntries = assetEntries;
            result.ReleaseAssets = assetEntries
                .Select(entry => entry.Path)
                .ToArray();
            return;
        }

        if (!string.IsNullOrWhiteSpace(stageRootTemplate))
        {
            var stageRoot = ResolveOutputPath(configDirectory, stageRootTemplate!);
            assetEntries = StageReleaseAssets(assetEntries, stageRoot, outputs.Staging).ToArray();
        }

        result.ReleaseAssetEntries = assetEntries;
        result.ReleaseAssets = assetEntries
            .Select(entry => entry.StagedPath ?? entry.Path)
            .ToArray();

        var manifestPathTemplate = outputs.ManifestJsonPath;
        var checksumsPathTemplate = outputs.ChecksumsPath;
        if (!string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            if (!string.IsNullOrWhiteSpace(manifestPathTemplate))
                manifestPathTemplate = CombineOutputRoot(request.OutputRoot!, manifestPathTemplate!);
            if (!string.IsNullOrWhiteSpace(checksumsPathTemplate))
                checksumsPathTemplate = CombineOutputRoot(request.OutputRoot!, checksumsPathTemplate!);
        }

        var manifestPath = string.IsNullOrWhiteSpace(manifestPathTemplate)
            ? null
            : ResolveOutputPath(configDirectory, manifestPathTemplate!);
        var checksumsPath = string.IsNullOrWhiteSpace(checksumsPathTemplate)
            ? null
            : ResolveOutputPath(configDirectory, checksumsPathTemplate!);

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            result.ReleaseManifestPath = manifestPath!;
            WriteReleaseManifest(result, manifestPath!);
        }

        if (!string.IsNullOrWhiteSpace(checksumsPath))
        {
            result.ReleaseChecksumsPath = checksumsPath!;
            WriteReleaseChecksums(result, checksumsPath!);
        }
    }

    private static bool ShouldPublishUnifiedGitHub(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        return spec.GitHub is not null && (request.PublishProjectGitHub ?? spec.GitHub.Publish);
    }

    private static string? ResolveConfiguredStageRoot(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory)
    {
        var stageRootTemplate = request.StageRoot ?? spec.Outputs?.Staging?.RootPath;
        if (string.IsNullOrWhiteSpace(stageRootTemplate))
            return null;

        return ResolveOutputPath(configDirectory, stageRootTemplate!);
    }

    private void GenerateWingetOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory,
        PowerForgeReleaseResult result)
    {
        var winget = spec.Winget;
        if (winget is null || !winget.Enabled || winget.Packages.Length == 0)
            return;

        var stageRoot = ResolveConfiguredStageRoot(spec, request, configDirectory);
        var outputPath = ResolveWingetOutputPath(winget, configDirectory, stageRoot);
        Directory.CreateDirectory(outputPath);

        var manifestPaths = new List<string>();
        foreach (var package in winget.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageIdentifier))
                throw new InvalidOperationException("Winget package PackageIdentifier is required.");
            if (string.IsNullOrWhiteSpace(package.Publisher))
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' is missing Publisher.");
            if (string.IsNullOrWhiteSpace(package.PackageName))
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' is missing PackageName.");
            if (string.IsNullOrWhiteSpace(package.License))
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' is missing License.");
            if (string.IsNullOrWhiteSpace(package.ShortDescription))
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' is missing ShortDescription.");

            var installerEntries = package.Installers
                .Select(installer => ResolveWingetInstallerEntry(installer, winget, package, result.ReleaseAssetEntries, result.ToolGitHubReleases))
                .ToArray();
            if (installerEntries.Length == 0)
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' did not resolve any installers.");

            var packageVersion = package.PackageVersion
                ?? installerEntries.Select(entry => entry.Asset.Version).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(packageVersion))
                throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' is missing PackageVersion and no installer asset version was available.");

            var manifestPath = Path.Combine(outputPath, $"{package.PackageIdentifier}.yaml");
            if (File.Exists(manifestPath))
                throw new InvalidOperationException($"Winget manifest already written for '{package.PackageIdentifier}'. PackageIdentifier values must be unique within a release config.");
            var yaml = BuildWingetManifestYaml(winget, package, packageVersion!, installerEntries);
            File.WriteAllText(manifestPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            manifestPaths.Add(manifestPath);
        }

        result.WingetManifestPaths = manifestPaths.ToArray();
    }

    private static string ResolveWingetOutputPath(
        PowerForgeReleaseWingetOptions winget,
        string configDirectory,
        string? stageRoot)
    {
        if (string.IsNullOrWhiteSpace(winget.OutputPath))
        {
            return !string.IsNullOrWhiteSpace(stageRoot)
                ? Path.Combine(stageRoot!, "Winget")
                : ResolveOutputPath(configDirectory, Path.Combine("Artifacts", "Winget"));
        }

        if (!Path.IsPathRooted(winget.OutputPath) && !string.IsNullOrWhiteSpace(stageRoot))
            return Path.GetFullPath(Path.Combine(stageRoot!, winget.OutputPath!));

        return ResolveOutputPath(configDirectory, winget.OutputPath!);
    }

    private static void IncludeWingetOutputsInReleaseAssets(PowerForgeReleaseResult result)
    {
        if (result.WingetManifestPaths.Length == 0)
            return;

        var entries = result.ReleaseAssetEntries.ToList();
        foreach (var path in result.WingetManifestPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(entry.StagedPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            entries.Add(new PowerForgeReleaseAssetEntry
            {
                Path = path,
                Category = PowerForgeReleaseAssetCategory.Other,
                Source = "Winget"
            });
        }

        result.ReleaseAssetEntries = entries.ToArray();
        result.ReleaseAssets = result.ReleaseAssetEntries
            .Select(entry => entry.StagedPath ?? entry.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static void RewriteReleaseSummaryFiles(PowerForgeReleaseResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ReleaseManifestPath))
            WriteReleaseManifest(result, result.ReleaseManifestPath!);
        if (!string.IsNullOrWhiteSpace(result.ReleaseChecksumsPath))
            WriteReleaseChecksums(result, result.ReleaseChecksumsPath!);
    }

    private PowerForgeUnifiedGitHubReleaseResult PublishUnifiedGitHubRelease(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
    {
        var gitHub = spec.GitHub ?? throw new InvalidOperationException("Unified GitHub release options were not configured.");
        var version = ResolveUnifiedReleaseVersion(result, sharedReleaseVersion);
        if (string.IsNullOrWhiteSpace(version))
        {
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Success = false,
                ErrorMessage = "Unable to resolve a shared release version for unified GitHub publishing."
            };
        }

        var resolved = ResolveUnifiedGitHubConfiguration(spec, gitHub, configDirectory);
        if (resolved.Error is not null)
            return resolved.Error;

        var assets = result.ReleaseAssets
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Concat(new[]
            {
                result.ReleaseManifestPath,
                result.ReleaseChecksumsPath
            }.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => path!)
            .ToArray();
        if (assets.Length == 0)
        {
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = resolved.Owner ?? string.Empty,
                Repository = resolved.Repository ?? string.Empty,
                Version = version!,
                Success = false,
                ErrorMessage = "No staged release assets were available for unified GitHub publishing."
            };
        }

        var tagTemplate = string.IsNullOrWhiteSpace(gitHub.TagTemplate)
            ? "v{Version}"
            : gitHub.TagTemplate!;
        var releaseNameTemplate = string.IsNullOrWhiteSpace(gitHub.ReleaseNameTemplate)
            ? "{Repository} {Version}"
            : gitHub.ReleaseNameTemplate!;
        var tagName = ApplyUnifiedGitHubTemplate(tagTemplate, resolved.Repository!, version!);
        var releaseName = ApplyUnifiedGitHubTemplate(releaseNameTemplate, resolved.Repository!, version!);

        try
        {
            var publishResult = _publishGitHubRelease(new GitHubReleasePublishRequest
            {
                Owner = resolved.Owner!,
                Repository = resolved.Repository!,
                Token = resolved.Token!,
                TagName = tagName,
                ReleaseName = releaseName,
                GenerateReleaseNotes = gitHub.GenerateReleaseNotes,
                IsPreRelease = gitHub.IsPreRelease,
                ReuseExistingReleaseOnConflict = true,
                AssetFilePaths = assets
            });

            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = resolved.Owner!,
                Repository = resolved.Repository!,
                Version = version!,
                TagName = tagName,
                ReleaseName = releaseName,
                AssetPaths = assets,
                Success = publishResult.Succeeded,
                ReleaseUrl = publishResult.HtmlUrl,
                ReusedExistingRelease = publishResult.ReusedExistingRelease,
                ErrorMessage = publishResult.Succeeded ? null : "Unified GitHub release publish failed.",
                SkippedExistingAssets = publishResult.SkippedExistingAssets?.ToArray() ?? Array.Empty<string>()
            };
        }
        catch (Exception ex)
        {
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = resolved.Owner ?? string.Empty,
                Repository = resolved.Repository ?? string.Empty,
                Version = version!,
                TagName = tagName,
                ReleaseName = releaseName,
                AssetPaths = assets,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private PowerForgeToolGitHubReleaseResult[] PublishLegacyToolGitHubReleases(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        PowerForgeToolReleaseResult result)
    {
        var gitHub = spec.Tools?.GitHub ?? new PowerForgeToolReleaseGitHubOptions();
        var resolved = ResolveGitHubConfiguration(spec, gitHub, configDirectory);
        if (resolved.Error is not null)
            return new[] { resolved.Error };

        var artefactGroups = result.Artefacts
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ZipPath))
            .GroupBy(entry => (Target: entry.Target, Version: entry.Version))
            .ToArray();

        if (artefactGroups.Length == 0)
        {
            return new[]
            {
                new PowerForgeToolGitHubReleaseResult
                {
                    Success = false,
                    ErrorMessage = "Tool GitHub publishing requires zip assets, but no ZipPath values were produced."
                }
            };
        }

        var results = new List<PowerForgeToolGitHubReleaseResult>();
        foreach (var group in artefactGroups)
        {
            var assets = group
                .Select(entry => entry.ZipPath!)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (assets.Length == 0)
            {
                results.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = group.Key.Target,
                    Version = group.Key.Version,
                    Success = false,
                    ErrorMessage = $"No zip assets found on disk for tool target '{group.Key.Target}'."
                });
                continue;
            }

            results.Add(PublishGitHubRelease(
                resolved.Owner!,
                resolved.Repository!,
                resolved.Token!,
                gitHub,
                group.Key.Target,
                group.Key.Version,
                assets));
        }

        return results.ToArray();
    }

    private PowerForgeToolGitHubReleaseResult[] PublishDotNetToolGitHubReleases(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        DotNetPublishPlan plan,
        DotNetPublishResult result,
        string? sharedReleaseVersion)
    {
        var gitHub = spec.Tools?.GitHub ?? new PowerForgeToolReleaseGitHubOptions();
        var resolved = ResolveGitHubConfiguration(spec, gitHub, configDirectory);
        if (resolved.Error is not null)
            return new[] { resolved.Error };

        var includeGlobalAssets = (plan.Targets?.Length ?? 0) == 1;
        var globalAssets = includeGlobalAssets
            ? new[]
            {
                result.ManifestJsonPath,
                result.ManifestTextPath,
                result.ChecksumsPath,
                result.RunReportPath
            }
            : Array.Empty<string?>();

        var releases = new List<PowerForgeToolGitHubReleaseResult>();
        foreach (var target in plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
        {
            var version = ResolveDotNetTargetVersion(target, result, sharedReleaseVersion);
            if (string.IsNullOrWhiteSpace(version))
            {
                releases.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = target.Name,
                    Success = false,
                    ErrorMessage = $"Unable to resolve version for DotNet publish target '{target.Name}'."
                });
                continue;
            }

            var assets = new List<string>();
            assets.AddRange(
                (result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.ZipPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!));

            assets.AddRange(
                (result.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
                .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.OutputFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)));

            assets.AddRange(
                (result.StorePackages ?? Array.Empty<DotNetPublishStorePackageResult>())
                .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => (entry.OutputFiles ?? Array.Empty<string>())
                    .Concat(entry.UploadFiles ?? Array.Empty<string>())
                    .Concat(entry.SymbolFiles ?? Array.Empty<string>()))
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)));

            assets.AddRange(
                globalAssets
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!));

            var uniqueAssets = assets
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (uniqueAssets.Length == 0)
            {
                releases.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = target.Name,
                    Version = version ?? string.Empty,
                    Success = false,
                    ErrorMessage = $"No GitHub-uploadable assets were produced for DotNet publish target '{target.Name}'."
                });
                continue;
            }

            releases.Add(PublishGitHubRelease(
                resolved.Owner!,
                resolved.Repository!,
                resolved.Token!,
                gitHub,
                target.Name,
                version!,
                uniqueAssets));
        }

        return releases.ToArray();
    }

    private PowerForgeToolGitHubReleaseResult PublishGitHubRelease(
        string owner,
        string repository,
        string token,
        PowerForgeToolReleaseGitHubOptions gitHub,
        string target,
        string version,
        string[] assets)
    {
        var tagTemplate = string.IsNullOrWhiteSpace(gitHub.TagTemplate)
            ? "{Target}-v{Version}"
            : gitHub.TagTemplate!;
        var releaseNameTemplate = string.IsNullOrWhiteSpace(gitHub.ReleaseNameTemplate)
            ? "{Target} {Version}"
            : gitHub.ReleaseNameTemplate!;

        var tagName = ApplyGitHubTemplate(tagTemplate, target, version, repository);
        var releaseName = ApplyGitHubTemplate(releaseNameTemplate, target, version, repository);

        try
        {
            var publishResult = _publishGitHubRelease(new GitHubReleasePublishRequest
            {
                Owner = owner,
                Repository = repository,
                Token = token,
                TagName = tagName,
                ReleaseName = releaseName,
                GenerateReleaseNotes = gitHub.GenerateReleaseNotes,
                IsPreRelease = gitHub.IsPreRelease,
                ReuseExistingReleaseOnConflict = true,
                AssetFilePaths = assets
            });

            return new PowerForgeToolGitHubReleaseResult
            {
                Owner = owner,
                Repository = repository,
                Target = target,
                Version = version,
                TagName = tagName,
                ReleaseName = releaseName,
                AssetPaths = assets,
                Success = publishResult.Succeeded,
                ReleaseUrl = publishResult.HtmlUrl,
                ReusedExistingRelease = publishResult.ReusedExistingRelease,
                ErrorMessage = publishResult.Succeeded ? null : "GitHub release publish failed.",
                SkippedExistingAssets = publishResult.SkippedExistingAssets?.ToArray() ?? Array.Empty<string>()
            };
        }
        catch (Exception ex)
        {
            return new PowerForgeToolGitHubReleaseResult
            {
                Owner = owner,
                Repository = repository,
                Target = target,
                Version = version,
                TagName = tagName,
                ReleaseName = releaseName,
                AssetPaths = assets,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string? ResolveDotNetTargetVersion(DotNetPublishTargetPlan target, DotNetPublishResult result, string? sharedReleaseVersion)
    {
        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return sharedReleaseVersion;

        if (!string.IsNullOrWhiteSpace(target.ProjectPath)
            && File.Exists(target.ProjectPath)
            && CsprojVersionEditor.TryGetVersion(target.ProjectPath, out var version)
            && !string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        var msiVersion = (result.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
            .Where(entry => string.Equals(entry.Target, target.Name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Version)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(msiVersion))
            return msiVersion;

        return string.IsNullOrWhiteSpace(sharedReleaseVersion) ? null : sharedReleaseVersion;
    }

    private static (string? Owner, string? Repository, string? Token, PowerForgeToolGitHubReleaseResult? Error) ResolveGitHubConfiguration(
        PowerForgeReleaseSpec spec,
        PowerForgeToolReleaseGitHubOptions gitHub,
        string configDirectory)
    {
        var owner = string.IsNullOrWhiteSpace(gitHub.Owner)
            ? spec.Packages?.GitHubUsername
            : gitHub.Owner!.Trim();
        var repository = string.IsNullOrWhiteSpace(gitHub.Repository)
            ? spec.Packages?.GitHubRepositoryName
            : gitHub.Repository!.Trim();
        var token = ProjectBuildSupportService.ResolveSecret(
            gitHub.Token,
            gitHub.TokenFilePath,
            gitHub.TokenEnvName,
            configDirectory);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return (null, null, null, new PowerForgeToolGitHubReleaseResult
            {
                Success = false,
                ErrorMessage = "Tool GitHub publishing requires Owner and Repository."
            });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, null, null, new PowerForgeToolGitHubReleaseResult
            {
                Success = false,
                ErrorMessage = "Tool GitHub publishing requires a token."
            });
        }

        return (owner, repository, token, null);
    }

    private static bool UsesDotNetToolWorkflow(PowerForgeToolReleaseSpec spec)
    {
        return spec.DotNetPublish is not null || !string.IsNullOrWhiteSpace(spec.DotNetPublishConfigPath);
    }

    private static (DotNetPublishSpec Spec, string SourceConfigPath) LoadDotNetToolsSpec(PowerForgeToolReleaseSpec tools, string releaseConfigPath)
    {
        if (tools.DotNetPublish is not null && !string.IsNullOrWhiteSpace(tools.DotNetPublishConfigPath))
            throw new InvalidOperationException("Tools.DotNetPublish and Tools.DotNetPublishConfigPath are mutually exclusive.");

        if (tools.DotNetPublish is not null)
        {
            if (!string.IsNullOrWhiteSpace(tools.DotNetPublishProfile))
                tools.DotNetPublish.Profile = tools.DotNetPublishProfile!.Trim();

            return (tools.DotNetPublish, releaseConfigPath);
        }

        if (string.IsNullOrWhiteSpace(tools.DotNetPublishConfigPath))
            throw new InvalidOperationException("DotNet publish tool workflow requires Tools.DotNetPublish or Tools.DotNetPublishConfigPath.");

        var releaseConfigDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.GetFullPath(Path.IsPathRooted(tools.DotNetPublishConfigPath)
            ? tools.DotNetPublishConfigPath!
            : Path.Combine(releaseConfigDirectory, tools.DotNetPublishConfigPath!));

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"DotNet publish config not found: {configPath}", configPath);

        var json = File.ReadAllText(configPath);
        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, DotNetToolsJsonOptions)
            ?? throw new InvalidOperationException($"DotNet publish config could not be deserialized: {configPath}");

        if (!string.IsNullOrWhiteSpace(tools.DotNetPublishProfile))
            spec.Profile = tools.DotNetPublishProfile!.Trim();

        return (spec, configPath);
    }

    private static DotNetPublishPlan PlanDotNetTools(
        ILogger logger,
        DotNetPublishSpec spec,
        string configPath,
        PowerForgeReleaseRequest request,
        ISet<PowerForgeReleaseToolOutputKind> selectedOutputs)
    {
        if (request.Flavors is { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Release --flavor overrides are only supported by legacy Tools.Targets workflows. " +
                "Use DotNet publish Styles in config when Tools uses DotNetPublish.");
        }

        ApplyDotNetRequestOverrides(spec, request);
        ApplyDotNetToolOutputSelection(spec, selectedOutputs);
        return new DotNetPublishPipelineRunner(logger).Plan(spec, configPath);
    }

    private static void ApplyDotNetRequestOverrides(DotNetPublishSpec spec, PowerForgeReleaseRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!string.IsNullOrWhiteSpace(request.Configuration))
        {
            spec.DotNet ??= new DotNetPublishDotNetOptions();
            spec.DotNet.Configuration = request.Configuration!.Trim();
        }

        if (request.AllowOutputOutsideProjectRoot || request.AllowManifestOutsideProjectRoot)
        {
            spec.DotNet ??= new DotNetPublishDotNetOptions();

            if (request.AllowOutputOutsideProjectRoot)
                spec.DotNet.AllowOutputOutsideProjectRoot = true;

            if (request.AllowManifestOutsideProjectRoot)
                spec.DotNet.AllowManifestOutsideProjectRoot = true;
        }

        if (!string.IsNullOrWhiteSpace(request.OutputRoot))
            ApplyDotNetOutputRootOverride(spec, request.OutputRoot!);

        var explicitlySelectedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.Targets is { Length: > 0 })
        {
            var selected = new HashSet<string>(
                request.Targets.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            explicitlySelectedTargets = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

            var targets = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
                .Where(target => target is not null)
                .ToArray();

            var missing = selected
                .Where(name => targets.All(target => !string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown DotNet publish target(s): {string.Join(", ", missing)}", nameof(request));

            var effectiveTargets = ExpandDotNetSelectedTargets(spec, selected);

            spec.Targets = targets
                .Where(target => effectiveTargets.Contains(target.Name))
                .ToArray();

            if (spec.Bundles is { Length: > 0 })
            {
                spec.Bundles = spec.Bundles
                    .Where(bundle =>
                        bundle is not null
                        && (!string.IsNullOrWhiteSpace(bundle.PrepareFromTarget)
                            ? selected.Contains(bundle.PrepareFromTarget)
                            : true))
                    .ToArray();
            }

            if (spec.Installers is { Length: > 0 })
            {
                spec.Installers = spec.Installers
                    .Where(installer =>
                        installer is not null
                        && (!string.IsNullOrWhiteSpace(installer.PrepareFromTarget)
                            ? selected.Contains(installer.PrepareFromTarget)
                            : true))
                    .ToArray();
            }

            if (spec.StorePackages is { Length: > 0 })
            {
                spec.StorePackages = spec.StorePackages
                    .Where(storePackage =>
                        storePackage is not null
                        && (!string.IsNullOrWhiteSpace(storePackage.PrepareFromTarget)
                            ? selected.Contains(storePackage.PrepareFromTarget)
                            : true))
                    .ToArray();
            }
        }

        if (request.Runtimes is { Length: > 0 })
        {
            var runtimes = request.Runtimes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();
                target.Publish.Runtimes = runtimes;
            }
        }

        if (request.Frameworks is { Length: > 0 })
        {
            var frameworks = request.Frameworks
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (frameworks.Length == 0)
                throw new ArgumentException("Framework overrides must not be empty.", nameof(request));

            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                if (target is null || !explicitlySelectedTargets.Contains(target.Name))
                    continue;

                target.Publish ??= new DotNetPublishPublishOptions();
                target.Publish.Framework = frameworks[0];
                target.Publish.Frameworks = frameworks;
            }
        }

        if (request.Styles is { Length: > 0 })
        {
            var styles = request.Styles
                .Distinct()
                .ToArray();

            if (styles.Length == 0)
                throw new ArgumentException("Style overrides must not be empty.", nameof(request));

            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();
                target.Publish.Style = styles[0];
                target.Publish.Styles = styles;
            }
        }

        if (request.KeepSymbols.HasValue)
        {
            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();
                target.Publish.KeepSymbols = request.KeepSymbols.Value;
            }
        }

        if (request.InstallerMsBuildProperties.Count > 0)
        {
            foreach (var installer in spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
            {
                installer.MsBuildProperties ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in request.InstallerMsBuildProperties)
                    installer.MsBuildProperties[entry.Key] = entry.Value;
            }
        }

        if (HasSigningOverrides(request))
        {
            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();
                var baseSign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    spec.SigningProfiles,
                    !string.IsNullOrWhiteSpace(request.SignProfile) ? request.SignProfile : target.Publish.SignProfile,
                    target.Publish.Sign,
                    target.Publish.SignOverrides,
                    $"Target '{target.Name}'");
                target.Publish.Sign = ApplySigningOverrides(baseSign, request);
                if (!string.IsNullOrWhiteSpace(request.SignProfile))
                {
                    target.Publish.SignProfile = null;
                    target.Publish.SignOverrides = null;
                }
            }

            foreach (var installer in spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
            {
                var baseSign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    spec.SigningProfiles,
                    !string.IsNullOrWhiteSpace(request.SignProfile) ? request.SignProfile : installer.SignProfile,
                    installer.Sign,
                    installer.SignOverrides,
                    $"Installer '{installer.Id}'");
                installer.Sign = ApplySigningOverrides(baseSign, request);
                if (!string.IsNullOrWhiteSpace(request.SignProfile))
                {
                    installer.SignProfile = null;
                    installer.SignOverrides = null;
                }
            }
        }
    }

    internal static HashSet<PowerForgeReleaseToolOutputKind> ResolveSelectedToolOutputs(PowerForgeReleaseRequest request)
    {
        var requestedOutputs = request.ToolOutputs ?? Array.Empty<PowerForgeReleaseToolOutputKind>();
        var skippedOutputs = request.SkipToolOutputs ?? Array.Empty<PowerForgeReleaseToolOutputKind>();

        var selected = requestedOutputs.Length > 0
            ? new HashSet<PowerForgeReleaseToolOutputKind>(requestedOutputs, EqualityComparer<PowerForgeReleaseToolOutputKind>.Default)
            : new HashSet<PowerForgeReleaseToolOutputKind>(
                ((PowerForgeReleaseToolOutputKind[])Enum.GetValues(typeof(PowerForgeReleaseToolOutputKind))),
                EqualityComparer<PowerForgeReleaseToolOutputKind>.Default);

        foreach (var skipped in skippedOutputs)
            selected.Remove(skipped);

        return selected;
    }

    internal static void ApplyDotNetToolOutputSelection(
        DotNetPublishSpec spec,
        ISet<PowerForgeReleaseToolOutputKind> selectedOutputs)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var includeTool = selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Tool);
        var includePortable = selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Portable);
        var includeInstaller = selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Installer);
        var includeStore = selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Store);

        foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
        {
            target.Publish ??= new DotNetPublishPublishOptions();
            if (!includeTool)
            {
                target.Publish.Zip = false;
                target.Publish.ZipPath = null;
                target.Publish.ZipNameTemplate = null;
            }
        }

        if (!includeInstaller)
            spec.Installers = Array.Empty<DotNetPublishInstaller>();

        if (!includeStore)
            spec.StorePackages = Array.Empty<DotNetPublishStorePackage>();

        if (spec.Bundles is not { Length: > 0 } || includePortable)
            return;

        var requiredBundleIds = includeInstaller
            ? new HashSet<string>(
                (spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
                    .Where(installer => !string.IsNullOrWhiteSpace(installer.PrepareFromBundleId))
                    .Select(installer => installer.PrepareFromBundleId!.Trim()),
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        spec.Bundles = spec.Bundles
            .Where(bundle => bundle is not null && requiredBundleIds.Contains(bundle.Id))
            .ToArray();

        foreach (var bundle in spec.Bundles)
        {
            bundle.Zip = false;
            bundle.ZipPath = null;
            bundle.ZipNameTemplate = null;
        }
    }

    internal static void FilterDotNetToolResult(
        DotNetPublishResult result,
        ISet<PowerForgeReleaseToolOutputKind> selectedOutputs)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        result.Artefacts = (result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .Where(artifact => IsSelectedDotNetArtefact(artifact, selectedOutputs))
            .ToArray();

        if (!selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Installer))
            result.MsiBuilds = Array.Empty<DotNetPublishMsiBuildResult>();

        if (!selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Store))
            result.StorePackages = Array.Empty<DotNetPublishStorePackageResult>();
    }

    internal static bool IsSelectedDotNetArtefact(
        DotNetPublishArtefactResult artifact,
        ISet<PowerForgeReleaseToolOutputKind> selectedOutputs)
    {
        if (artifact.Category == DotNetPublishArtefactCategory.Bundle)
            return selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Portable);

        return selectedOutputs.Contains(PowerForgeReleaseToolOutputKind.Tool);
    }

    private static void ApplyDotNetOutputRootOverride(DotNetPublishSpec spec, string outputRoot)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var normalizedRoot = (outputRoot ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalizedRoot))
            return;

        foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
        {
            if (target?.Publish is null)
                continue;

            target.Publish.OutputPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(target.Publish.OutputPath)
                    ? DefaultDotNetTargetOutputTemplate
                    : target.Publish.OutputPath!);

            if (!string.IsNullOrWhiteSpace(target.Publish.ZipPath))
                target.Publish.ZipPath = CombineOutputRoot(normalizedRoot, target.Publish.ZipPath!);
        }

        foreach (var bundle in spec.Bundles ?? Array.Empty<DotNetPublishBundle>())
        {
            if (bundle is null)
                continue;

            bundle.OutputPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(bundle.OutputPath)
                    ? DefaultDotNetBundleOutputTemplate
                    : bundle.OutputPath!);

            if (!string.IsNullOrWhiteSpace(bundle.ZipPath))
                bundle.ZipPath = CombineOutputRoot(normalizedRoot, bundle.ZipPath!);
        }

        foreach (var installer in spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
        {
            if (installer is null)
                continue;

            installer.StagingPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(installer.StagingPath)
                    ? DefaultMsiPrepareStagingPathTemplate
                    : installer.StagingPath!);
            installer.ManifestPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(installer.ManifestPath)
                    ? DefaultMsiPrepareManifestPathTemplate
                    : installer.ManifestPath!);
            installer.HarvestPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(installer.HarvestPath)
                    ? DefaultMsiHarvestPathTemplate
                    : installer.HarvestPath!);
        }

        foreach (var storePackage in spec.StorePackages ?? Array.Empty<DotNetPublishStorePackage>())
        {
            if (storePackage is null)
                continue;

            storePackage.OutputPath = CombineOutputRoot(
                normalizedRoot,
                string.IsNullOrWhiteSpace(storePackage.OutputPath)
                    ? DefaultStorePackageOutputTemplate
                    : storePackage.OutputPath!);
        }

        spec.Outputs ??= new DotNetPublishOutputs();
        spec.Outputs.ManifestJsonPath = CombineOutputRoot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(spec.Outputs.ManifestJsonPath)
                ? DefaultDotNetManifestJsonTemplate
                : spec.Outputs.ManifestJsonPath!);
        spec.Outputs.ManifestTextPath = CombineOutputRoot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(spec.Outputs.ManifestTextPath)
                ? DefaultDotNetManifestTextTemplate
                : spec.Outputs.ManifestTextPath!);
        spec.Outputs.ChecksumsPath = CombineOutputRoot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(spec.Outputs.ChecksumsPath)
                ? DefaultDotNetChecksumsTemplate
                : spec.Outputs.ChecksumsPath!);
        spec.Outputs.RunReportPath = CombineOutputRoot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(spec.Outputs.RunReportPath)
                ? DefaultDotNetRunReportTemplate
                : spec.Outputs.RunReportPath!);
    }

    private static string CombineOutputRoot(string outputRoot, string path)
    {
        var root = (outputRoot ?? string.Empty).Trim().Trim('"');
        var child = (path ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(root))
            return child;
        if (string.IsNullOrWhiteSpace(child))
            return root;
        if (Path.IsPathRooted(child))
            return child;

        return Path.Combine(root, child);
    }

    private static HashSet<string> ExpandDotNetSelectedTargets(
        DotNetPublishSpec spec,
        HashSet<string> selected)
    {
        var effectiveTargets = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in spec.Bundles ?? Array.Empty<DotNetPublishBundle>())
        {
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.PrepareFromTarget))
                continue;
            if (!selected.Contains(bundle.PrepareFromTarget))
                continue;

            foreach (var include in bundle.Includes ?? Array.Empty<DotNetPublishBundleInclude>())
            {
                if (include is null || string.IsNullOrWhiteSpace(include.Target))
                    continue;

                effectiveTargets.Add(include.Target);
            }
        }

        return effectiveTargets;
    }

    private static string ApplyGitHubTemplate(string template, string target, string version, string repository)
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        return template
            .Replace("{Target}", target)
            .Replace("{Project}", target)
            .Replace("{Version}", version)
            .Replace("{Repo}", repository)
            .Replace("{Repository}", repository)
            .Replace("{Date}", now.ToString("yyyy.MM.dd"))
            .Replace("{UtcDate}", utcNow.ToString("yyyy.MM.dd"))
            .Replace("{DateTime}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcDateTime}", utcNow.ToString("yyyyMMddHHmmss"))
            .Replace("{Timestamp}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcTimestamp}", utcNow.ToString("yyyyMMddHHmmss"));
    }

    private static string ApplyUnifiedGitHubTemplate(string template, string repository, string version)
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        return template
            .Replace("{Target}", string.Empty)
            .Replace("{Project}", string.Empty)
            .Replace("{Version}", version)
            .Replace("{Repo}", repository)
            .Replace("{Repository}", repository)
            .Replace("{Date}", now.ToString("yyyy.MM.dd"))
            .Replace("{UtcDate}", utcNow.ToString("yyyy.MM.dd"))
            .Replace("{DateTime}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcDateTime}", utcNow.ToString("yyyyMMddHHmmss"))
            .Replace("{Timestamp}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcTimestamp}", utcNow.ToString("yyyyMMddHHmmss"));
    }

    private static string? ResolveUnifiedReleaseVersion(PowerForgeReleaseResult result, string? sharedReleaseVersion)
    {
        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return sharedReleaseVersion;

        var versions = result.ReleaseAssetEntries
            .Select(entry => entry.Version)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (versions.Length == 1)
            return versions[0];

        return null;
    }

    private static (string? Owner, string? Repository, string? Token, PowerForgeUnifiedGitHubReleaseResult? Error) ResolveUnifiedGitHubConfiguration(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseGitHubOptions gitHub,
        string configDirectory)
    {
        var owner = FirstNonEmpty(gitHub.Owner, spec.Packages?.GitHubUsername)?.Trim();
        var repository = FirstNonEmpty(gitHub.Repository, spec.Packages?.GitHubRepositoryName)?.Trim();
        var token = ResolveSecret(FirstNonEmpty(gitHub.Token, spec.Packages?.GitHubAccessToken), FirstNonEmpty(gitHub.TokenFilePath, spec.Packages?.GitHubAccessTokenFilePath), FirstNonEmpty(gitHub.TokenEnvName, spec.Packages?.GitHubAccessTokenEnvName), configDirectory);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return (owner, repository, token, new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = owner ?? string.Empty,
                Repository = repository ?? string.Empty,
                Success = false,
                ErrorMessage = "Unified GitHub release publishing requires Owner and Repository (or package GitHub defaults)."
            });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return (owner, repository, token, new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = owner!,
                Repository = repository!,
                Success = false,
                ErrorMessage = "Unified GitHub release publishing requires a GitHub token (direct value, file, or environment variable)."
            });
        }

        return (owner, repository, token, null);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ResolveSecret(string? directValue, string? filePath, string? envName, string configDirectory)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
            return directValue!.Trim();

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var resolvedPath = ResolveOutputPath(configDirectory, filePath!);
            if (File.Exists(resolvedPath))
            {
                var fileValue = File.ReadAllText(resolvedPath).Trim();
                if (!string.IsNullOrWhiteSpace(fileValue))
                    return fileValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(envName))
        {
            var envValue = Environment.GetEnvironmentVariable(envName!.Trim());
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue.Trim();
        }

        return null;
    }

    private static PowerForgeReleaseAssetEntry[] CollectReleaseAssetEntries(
        PowerForgeReleaseResult result,
        DotNetPublishPlan? dotNetPlan,
        string? sharedReleaseVersion)
    {
        var assets = new List<PowerForgeReleaseAssetEntry>();

        assets.AddRange(
            (result.ModuleAssets ?? Array.Empty<string>())
            .SelectMany(CreateModuleAssetEntries));

        assets.AddRange(
            (result.Packages?.Result.Release?.Projects ?? new List<DotNetRepositoryProjectResult>())
            .SelectMany(project => CreatePackageAssetEntries(project)));

        assets.AddRange(
            (result.Tools?.Artefacts ?? Array.Empty<PowerForgeToolReleaseArtifactResult>())
            .SelectMany(artifact => CreateLegacyToolAssetEntries(artifact)));

        assets.AddRange(
            result.Tools?.ManifestPaths
            ?.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "LegacyTools"
            })
            ?? Array.Empty<PowerForgeReleaseAssetEntry>());

        assets.AddRange(
            (result.DotNetTools?.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .SelectMany(artifact => CreateDotNetArtefactEntries(artifact, dotNetPlan, sharedReleaseVersion)));

        assets.AddRange(
            (result.DotNetTools?.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
            .SelectMany(build => (build.OutputFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => new { Path = path!, Build = build }))
            .Select(item => new PowerForgeReleaseAssetEntry
            {
                Path = item.Path,
                Category = PowerForgeReleaseAssetCategory.Installer,
                Source = "DotNetPublish",
                Target = item.Build.Target,
                Version = item.Build.Version,
                Runtime = item.Build.Runtime,
                Framework = item.Build.Framework,
                Style = item.Build.Style.ToString()
            }));

        assets.AddRange(
            (result.DotNetTools?.StorePackages ?? Array.Empty<DotNetPublishStorePackageResult>())
            .SelectMany(CreateDotNetStorePackageEntries));

        assets.AddRange(
            new[]
            {
                result.DotNetTools?.ManifestJsonPath,
                result.DotNetTools?.ManifestTextPath,
                result.DotNetTools?.ChecksumsPath,
                result.DotNetTools?.RunReportPath
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "DotNetPublish"
            }));

        return assets.ToArray();
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreatePackageAssetEntries(DotNetRepositoryProjectResult project)
    {
        foreach (var package in project.Packages.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = package,
                Category = PowerForgeReleaseAssetCategory.Package,
                Source = "Packages",
                Target = project.ProjectName,
                PackageId = project.PackageId,
                Version = project.NewVersion
            };
        }

        if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath) && File.Exists(project.ReleaseZipPath))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = project.ReleaseZipPath!,
                Category = PowerForgeReleaseAssetCategory.Package,
                Source = "Packages",
                Target = project.ProjectName,
                PackageId = project.PackageId,
                Version = project.NewVersion
            };
        }
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateLegacyToolAssetEntries(PowerForgeToolReleaseArtifactResult artifact)
    {
        foreach (var path in new[] { artifact.ZipPath, artifact.ExecutablePath, artifact.CommandAliasPath }
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Tool,
                Source = "LegacyTools",
                Target = artifact.Target,
                Version = artifact.Version,
                Runtime = artifact.Runtime,
                Framework = artifact.Framework,
                Style = artifact.Flavor.ToString()
            };
        }
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetArtefactEntries(
        DotNetPublishArtefactResult artifact,
        DotNetPublishPlan? dotNetPlan,
        string? sharedReleaseVersion)
    {
        if (string.IsNullOrWhiteSpace(artifact.ZipPath) || !File.Exists(artifact.ZipPath))
            yield break;

        var version = ResolveDotNetArtefactVersion(artifact, dotNetPlan, sharedReleaseVersion);

        yield return new PowerForgeReleaseAssetEntry
        {
            Path = artifact.ZipPath!,
            Category = artifact.Category == DotNetPublishArtefactCategory.Bundle
                ? PowerForgeReleaseAssetCategory.Portable
                : PowerForgeReleaseAssetCategory.Tool,
            Source = "DotNetPublish",
            Target = artifact.Target,
            Version = version,
            Runtime = artifact.Runtime,
            Framework = artifact.Framework,
            Style = artifact.Style.ToString(),
            BundleId = artifact.BundleId
        };
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetStorePackageEntries(DotNetPublishStorePackageResult storePackage)
    {
        foreach (var path in (storePackage.OutputFiles ?? Array.Empty<string>())
            .Concat(storePackage.UploadFiles ?? Array.Empty<string>())
            .Concat(storePackage.SymbolFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Store,
                Source = "DotNetPublish",
                Target = storePackage.Target,
                Runtime = storePackage.Runtime,
                Framework = storePackage.Framework,
                Style = storePackage.Style.ToString()
            };
        }
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateModuleAssetEntries(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        if (!File.Exists(path) && !Directory.Exists(path))
            yield break;

        yield return new PowerForgeReleaseAssetEntry
        {
            Path = path,
            Category = PowerForgeReleaseAssetCategory.Module,
            Source = "Module"
        };
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> StageReleaseAssets(
        IEnumerable<PowerForgeReleaseAssetEntry> assetEntries,
        string stageRoot,
        PowerForgeReleaseStagingOptions? stagingOptions)
    {
        var options = stagingOptions ?? new PowerForgeReleaseStagingOptions();
        Directory.CreateDirectory(stageRoot);

        foreach (var entry in assetEntries)
        {
            var sourcePath = entry.Path;
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            var sourceIsFile = File.Exists(sourcePath);
            var sourceIsDirectory = !sourceIsFile && Directory.Exists(sourcePath);
            if (!sourceIsFile && !sourceIsDirectory)
                continue;

            var categoryDirectory = ResolveStageDirectory(options, entry.Category);
            var relativeStagePath = Path.Combine(categoryDirectory, GetStageEntryName(entry, sourceIsDirectory, options));
            var destinationPath = Path.Combine(stageRoot, relativeStagePath);
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var destinationFullPath = Path.GetFullPath(destinationPath);

            entry.RelativeStagePath = relativeStagePath.Replace('\\', '/');
            entry.StagedPath = destinationFullPath;

            if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
            if (sourceIsFile)
            {
                File.Copy(sourceFullPath, destinationFullPath, overwrite: true);
            }
            else
            {
                if (Directory.Exists(destinationFullPath))
                    Directory.Delete(destinationFullPath, recursive: true);
                CopyDirectory(sourceFullPath, destinationFullPath);
            }
        }

        return assetEntries;
    }

    private static string ResolveStageDirectory(PowerForgeReleaseStagingOptions options, PowerForgeReleaseAssetCategory category)
    {
        return category switch
        {
            PowerForgeReleaseAssetCategory.Module => NormalizeStageSegment(options.ModulesPath, "modules"),
            PowerForgeReleaseAssetCategory.Package => NormalizeStageSegment(options.PackagesPath, "nuget"),
            PowerForgeReleaseAssetCategory.Portable => NormalizeStageSegment(options.PortablePath, "portable"),
            PowerForgeReleaseAssetCategory.Installer => NormalizeStageSegment(options.InstallerPath, "installer"),
            PowerForgeReleaseAssetCategory.Store => NormalizeStageSegment(options.StorePath, "store"),
            PowerForgeReleaseAssetCategory.Tool => NormalizeStageSegment(options.ToolsPath, "tools"),
            PowerForgeReleaseAssetCategory.Metadata => NormalizeStageSegment(options.MetadataPath, "metadata"),
            _ => NormalizeStageSegment(options.OtherPath, "assets")
        };
    }

    private static string NormalizeStageSegment(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('\\', '/');
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static ResolvedWingetInstallerEntry ResolveWingetInstallerEntry(
        PowerForgeReleaseWingetInstaller installer,
        PowerForgeReleaseWingetOptions winget,
        PowerForgeReleaseWingetPackage package,
        IReadOnlyList<PowerForgeReleaseAssetEntry> assets,
        IReadOnlyList<PowerForgeToolGitHubReleaseResult> toolGitHubReleases)
    {
        var asset = assets.FirstOrDefault(candidate =>
            candidate.Category == installer.Category &&
            (string.IsNullOrWhiteSpace(installer.Target) || string.Equals(candidate.Target, installer.Target, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(installer.Runtime) || string.Equals(candidate.Runtime, installer.Runtime, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(installer.Framework) || string.Equals(candidate.Framework, installer.Framework, StringComparison.OrdinalIgnoreCase)));

        if (asset is null)
        {
            throw new InvalidOperationException(
                $"Winget package '{package.PackageIdentifier}' could not match an asset for Category={installer.Category}, Target={installer.Target ?? "*"}, Runtime={installer.Runtime ?? "*"}, Framework={installer.Framework ?? "*"}.");
        }

        var installerPath = asset.StagedPath ?? asset.Path;
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException($"Winget asset does not exist on disk: {installerPath}");

        var fileName = Path.GetFileName(installerPath);
        var version = package.PackageVersion ?? asset.Version ?? string.Empty;
        var architecture = string.IsNullOrWhiteSpace(installer.Architecture)
            ? InferWingetArchitecture(asset.Runtime)
            : installer.Architecture!.Trim();
        var urlTemplate = string.IsNullOrWhiteSpace(installer.UrlTemplate)
            ? winget.InstallerUrlTemplate
            : installer.UrlTemplate;
        var url = !string.IsNullOrWhiteSpace(urlTemplate)
            ? ApplyWingetUrlTemplate(
                urlTemplate!,
                package.PackageIdentifier,
                version,
                fileName,
                asset.Target,
                asset.Runtime,
                asset.Framework)
            : ResolveGitHubReleaseDownloadUrl(asset, toolGitHubReleases);
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' requires InstallerUrlTemplate, installer UrlTemplate, or matching PublishToolGitHub release output.");
        var resolvedUrl = url!;

        return new ResolvedWingetInstallerEntry
        {
            Asset = asset,
            Architecture = architecture,
            InstallerType = string.IsNullOrWhiteSpace(installer.InstallerType) ? "zip" : installer.InstallerType,
            NestedInstallerType = installer.NestedInstallerType,
            RelativeFilePath = installer.RelativeFilePath,
            InstallerUrl = resolvedUrl,
            InstallerSha256 = ComputeSha256(installerPath)
        };
    }

    private static string? ResolveGitHubReleaseDownloadUrl(
        PowerForgeReleaseAssetEntry asset,
        IReadOnlyList<PowerForgeToolGitHubReleaseResult> toolGitHubReleases)
    {
        var release = toolGitHubReleases.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.TagName) &&
            !string.IsNullOrWhiteSpace(candidate.Owner) &&
            !string.IsNullOrWhiteSpace(candidate.Repository) &&
            string.Equals(candidate.Target, asset.Target, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(asset.Version) || string.Equals(candidate.Version, asset.Version, StringComparison.OrdinalIgnoreCase)));
        if (release is null)
            return null;

        var fileName = Path.GetFileName(asset.StagedPath ?? asset.Path);
        return $"https://github.com/{release.Owner}/{release.Repository}/releases/download/{Uri.EscapeDataString(release.TagName)}/{Uri.EscapeDataString(fileName)}";
    }

    private static string BuildWingetManifestYaml(
        PowerForgeReleaseWingetOptions winget,
        PowerForgeReleaseWingetPackage package,
        string packageVersion,
        IReadOnlyList<ResolvedWingetInstallerEntry> installers)
    {
        var builder = new StringBuilder();
        var packageLocale = string.IsNullOrWhiteSpace(package.PackageLocale) ? (winget.PackageLocale ?? "en-US") : package.PackageLocale!;
        var manifestVersion = string.IsNullOrWhiteSpace(package.ManifestVersion) ? (winget.ManifestVersion ?? "1.12.0") : package.ManifestVersion!;
        AppendYamlLine(builder, "PackageIdentifier", package.PackageIdentifier);
        AppendYamlLine(builder, "PackageVersion", packageVersion);
        AppendYamlLine(builder, "PackageLocale", packageLocale);
        AppendYamlLine(builder, "Publisher", package.Publisher);
        AppendOptionalYamlLine(builder, "PublisherUrl", package.PublisherUrl);
        AppendYamlLine(builder, "PackageName", package.PackageName);
        AppendOptionalYamlLine(builder, "PackageUrl", package.PackageUrl);
        AppendYamlLine(builder, "License", package.License);
        AppendOptionalYamlLine(builder, "LicenseUrl", package.LicenseUrl);
        AppendYamlLine(builder, "ShortDescription", package.ShortDescription);
        AppendOptionalYamlLine(builder, "Moniker", package.Moniker);
        AppendYamlArray(builder, "Tags", package.Tags);
        AppendYamlArray(builder, "Platform", package.Platform);
        AppendOptionalYamlLine(builder, "MinimumOSVersion", package.MinimumOSVersion);
        var installerType = installers[0].InstallerType;
        var distinctInstallerTypes = installers
            .Select(entry => entry.InstallerType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctInstallerTypes.Length > 1)
            throw new InvalidOperationException($"Winget package '{package.PackageIdentifier}' resolved mixed InstallerType values ({string.Join(", ", distinctInstallerTypes)}), which is not supported in singleton manifests.");
        AppendYamlLine(builder, "InstallerType", installerType);
        builder.AppendLine("Installers:");
        foreach (var installer in installers)
        {
            AppendYamlSequenceLine(builder, "Architecture", installer.Architecture);
            AppendYamlLine(builder, "InstallerUrl", installer.InstallerUrl, indent: 2);
            AppendYamlLine(builder, "InstallerSha256", installer.InstallerSha256, indent: 2);
            if (!string.IsNullOrWhiteSpace(installer.NestedInstallerType))
                AppendYamlLine(builder, "NestedInstallerType", installer.NestedInstallerType!, indent: 2);
            if (!string.IsNullOrWhiteSpace(installer.RelativeFilePath))
            {
                builder.AppendLine("  NestedInstallerFiles:");
                AppendYamlSequenceLine(builder, "RelativeFilePath", installer.RelativeFilePath!, indent: 2);
            }
        }

        AppendYamlLine(builder, "ManifestType", "singleton");
        AppendYamlLine(builder, "ManifestVersion", manifestVersion);
        return builder.ToString();
    }

    private static void AppendYamlLine(StringBuilder builder, string key, string value, int indent = 0)
    {
        if (indent > 0)
            builder.Append(' ', indent);
        builder.Append(key);
        builder.Append(": ");
        builder.AppendLine(EscapeYamlScalar(value));
    }

    private static void AppendOptionalYamlLine(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            AppendYamlLine(builder, key, value!);
    }

    private static void AppendYamlArray(StringBuilder builder, string key, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return;

        builder.AppendLine($"{key}:");
        foreach (var value in values.Where(static value => !string.IsNullOrWhiteSpace(value)))
            builder.AppendLine($"- {EscapeYamlScalar(value)}");
    }

    private static void AppendYamlSequenceLine(StringBuilder builder, string key, string value, int indent = 0)
    {
        if (indent > 0)
            builder.Append(' ', indent);
        builder.Append("- ");
        builder.Append(key);
        builder.Append(": ");
        builder.AppendLine(EscapeYamlScalar(value));
    }

    private static string ApplyWingetUrlTemplate(
        string template,
        string packageIdentifier,
        string packageVersion,
        string fileName,
        string? target,
        string? runtime,
        string? framework)
    {
        return template
            .Replace("{PackageIdentifier}", Uri.EscapeDataString(packageIdentifier))
            .Replace("{PackageVersion}", Uri.EscapeDataString(packageVersion))
            .Replace("{FileName}", Uri.EscapeDataString(fileName))
            .Replace("{Target}", Uri.EscapeDataString(target ?? string.Empty))
            .Replace("{Runtime}", Uri.EscapeDataString(runtime ?? string.Empty))
            .Replace("{Framework}", Uri.EscapeDataString(framework ?? string.Empty));
    }

    private static string EscapeYamlScalar(string value)
    {
        var normalized = value.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        if (normalized.Length == 0)
            return "\"\"";

        return normalized.IndexOfAny(new[] { ':', '#', '{', '}', '[', ']', ',', '&', '*', '?', '|', '-', '<', '>', '=', '!', '%', '@', '\\', '"' }) >= 0
            || normalized.Contains(' ')
            ? "\"" + normalized.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : normalized;
    }

    private static string InferWingetArchitecture(string? runtime)
    {
        var normalized = (runtime ?? string.Empty).Trim();
        if (normalized.EndsWith("arm64", StringComparison.OrdinalIgnoreCase))
            return "arm64";
        if (normalized.EndsWith("x64", StringComparison.OrdinalIgnoreCase))
            return "x64";
        if (normalized.EndsWith("x86", StringComparison.OrdinalIgnoreCase))
            return "x86";

        throw new InvalidOperationException($"Could not infer Winget architecture from runtime '{runtime ?? "<null>"}'. Set the installer Architecture explicitly.");
    }

    private static string? ResolveDotNetArtefactVersion(DotNetPublishArtefactResult artifact, DotNetPublishPlan? plan, string? sharedReleaseVersion)
    {
        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return sharedReleaseVersion;

        if (plan?.Targets is null)
            return null;

        var target = plan.Targets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, artifact.Target, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return null;

        var version = !string.IsNullOrWhiteSpace(target.ProjectPath)
            && File.Exists(target.ProjectPath)
            && CsprojVersionEditor.TryGetVersion(target.ProjectPath, out var resolvedVersion)
            && !string.IsNullOrWhiteSpace(resolvedVersion)
            ? resolvedVersion
            : null;

        return version;
    }

    private sealed class ResolvedWingetInstallerEntry
    {
        public PowerForgeReleaseAssetEntry Asset { get; set; } = new();

        public string Architecture { get; set; } = string.Empty;

        public string InstallerType { get; set; } = "zip";

        public string? NestedInstallerType { get; set; }

        public string? RelativeFilePath { get; set; }

        public string InstallerUrl { get; set; } = string.Empty;

        public string InstallerSha256 { get; set; } = string.Empty;
    }

    private static object? BuildPackageManifestSection(ProjectBuildHostExecutionResult? packages)
    {
        var release = packages?.Result.Release;
        if (release is null)
            return null;

        return new
        {
            release.ResolvedVersion,
            PublishedPackages = release.PublishedPackages.ToArray(),
            Projects = release.Projects.Select(project => new
            {
                project.ProjectName,
                project.PackageId,
                project.NewVersion,
                Packages = project.Packages.ToArray(),
                project.ReleaseZipPath
            }).ToArray()
        };
    }

    private static void WriteReleaseManifest(PowerForgeReleaseResult result, string manifestPath)
    {
        var manifest = new
        {
            schemaVersion = 1,
            createdUtc = DateTime.UtcNow.ToString("o"),
            configPath = result.ConfigPath,
            assets = result.ReleaseAssets,
            assetEntries = result.ReleaseAssetEntries.Select(entry => new
            {
                entry.Path,
                category = entry.Category.ToString(),
                entry.Source,
                entry.Target,
                entry.PackageId,
                entry.Version,
                entry.Runtime,
                entry.Framework,
                entry.Style,
                entry.BundleId,
                entry.RelativeStagePath,
                entry.StagedPath
            }).ToArray(),
            module = BuildModuleManifestSection(result.ModulePlan, result.Module, result.ModuleAssets),
            packages = BuildPackageManifestSection(result.Packages),
            legacyTools = BuildLegacyToolsManifestSection(result.Tools),
            dotNetTools = BuildDotNetToolsManifestSection(result.DotNetTools),
            githubReleases = result.ToolGitHubReleases,
            unifiedGithubRelease = result.UnifiedGitHubRelease
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(manifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteReleaseChecksums(PowerForgeReleaseResult result, string checksumsPath)
    {
        var checksumInputs = new List<string>(result.ReleaseAssets);
        if (!string.IsNullOrWhiteSpace(result.ReleaseManifestPath) && File.Exists(result.ReleaseManifestPath))
            checksumInputs.Add(result.ReleaseManifestPath!);

        var uniqueChecksumInputs = checksumInputs
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(checksumsPath)!);
        var relativeTo = Path.GetDirectoryName(checksumsPath)!;
        var lines = uniqueChecksumInputs
            .Select(path => $"{ComputeSha256(path!)} *{GetRelativePathCompat(relativeTo, path!).Replace('\\', '/')}")
            .ToArray();
        File.WriteAllLines(checksumsPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static object? BuildLegacyToolsManifestSection(PowerForgeToolReleaseResult? tools)
    {
        if (tools is null)
            return null;

        return new
        {
            Artefacts = tools.Artefacts,
            tools.ManifestPaths
        };
    }

    private static object? BuildDotNetToolsManifestSection(DotNetPublishResult? tools)
    {
        if (tools is null)
            return null;

        return new
        {
            tools.ManifestJsonPath,
            tools.ManifestTextPath,
            tools.ChecksumsPath,
            tools.RunReportPath,
            tools.Artefacts,
            tools.MsiBuilds,
            tools.StorePackages
        };
    }

    private static object? BuildModuleManifestSection(
        PowerForgeModuleReleasePlanSummary? modulePlan,
        ModuleBuildHostExecutionResult? module,
        string[]? moduleAssets)
    {
        var assets = (moduleAssets ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (modulePlan is null && module is null && assets.Length == 0)
            return null;

        return new
        {
            plan = modulePlan,
            module?.Succeeded,
            module?.ExitCode,
            duration = module?.Duration,
            module?.Executable,
            Assets = assets
        };
    }

    private static string GetStageEntryName(
        PowerForgeReleaseAssetEntry entry,
        bool isDirectory,
        PowerForgeReleaseStagingOptions options)
    {
        var sourcePath = entry.Path;
        var defaultName = !isDirectory
            ? Path.GetFileName(sourcePath)
            : Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var template = ResolveStageNameTemplate(options, entry.Category);
        if (string.IsNullOrWhiteSpace(template))
            return defaultName;

        var extension = isDirectory ? string.Empty : Path.GetExtension(sourcePath);
        var fileNameWithoutExtension = isDirectory
            ? defaultName
            : Path.GetFileNameWithoutExtension(sourcePath);
        var name = template!
            .Replace("{FileName}", defaultName)
            .Replace("{FileNameWithoutExtension}", fileNameWithoutExtension)
            .Replace("{Extension}", extension)
            .Replace("{Category}", entry.Category.ToString())
            .Replace("{Source}", entry.Source ?? string.Empty)
            .Replace("{Target}", entry.Target ?? string.Empty)
            .Replace("{PackageId}", entry.PackageId ?? string.Empty)
            .Replace("{Version}", entry.Version ?? string.Empty)
            .Replace("{Runtime}", entry.Runtime ?? string.Empty)
            .Replace("{Framework}", entry.Framework ?? string.Empty)
            .Replace("{Style}", entry.Style ?? string.Empty)
            .Replace("{BundleId}", entry.BundleId ?? string.Empty);
        name = SanitizeStageEntryName(name);

        if (!isDirectory && !string.IsNullOrWhiteSpace(extension) && string.IsNullOrWhiteSpace(Path.GetExtension(name)))
            name += extension;

        return string.IsNullOrWhiteSpace(name) ? defaultName : name;
    }

    private static string? ResolveStageNameTemplate(PowerForgeReleaseStagingOptions options, PowerForgeReleaseAssetCategory category)
    {
        return category switch
        {
            PowerForgeReleaseAssetCategory.Module => options.ModulesNameTemplate,
            PowerForgeReleaseAssetCategory.Package => options.PackagesNameTemplate,
            PowerForgeReleaseAssetCategory.Portable => options.PortableNameTemplate,
            PowerForgeReleaseAssetCategory.Installer => options.InstallerNameTemplate,
            PowerForgeReleaseAssetCategory.Store => options.StoreNameTemplate,
            PowerForgeReleaseAssetCategory.Tool => options.ToolsNameTemplate,
            PowerForgeReleaseAssetCategory.Metadata => options.MetadataNameTemplate,
            _ => options.OtherNameTemplate
        };
    }

    private static string SanitizeStageEntryName(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '-');

        return normalized
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-');
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePathCompat(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePathCompat(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string ResolveOutputPath(string configDirectory, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(configDirectory, path));
    }

    private static string? NormalizeConfiguration(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return null;

        var normalized = configuration!.Trim();
        if (!string.Equals(normalized, "Release", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported release configuration override: {configuration}. Expected Release or Debug.", nameof(configuration));
        }

        return normalized;
    }

    private static void ApplyPackageRequestOverrides(
        ProjectBuildConfiguration packages,
        PowerForgeReleaseRequest request,
        string? configurationOverride)
    {
        if (packages is null)
            throw new ArgumentNullException(nameof(packages));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!string.IsNullOrWhiteSpace(configurationOverride))
            packages.Configuration = configurationOverride;
        if (!string.IsNullOrWhiteSpace(request.PackageSignThumbprint))
            packages.CertificateThumbprint = request.PackageSignThumbprint!.Trim();
        if (!string.IsNullOrWhiteSpace(request.PackageSignStore))
            packages.CertificateStore = request.PackageSignStore!.Trim();
        if (!string.IsNullOrWhiteSpace(request.PackageSignTimestampUrl))
            packages.TimeStampServer = request.PackageSignTimestampUrl!.Trim();
    }

    private static void ApplyToolRequestOverrides(PowerForgeToolReleaseSpec tools, PowerForgeReleaseRequest request, string? configurationOverride)
    {
        if (tools is null)
            throw new ArgumentNullException(nameof(tools));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!string.IsNullOrWhiteSpace(configurationOverride))
            tools.Configuration = configurationOverride!;

        if (request.KeepSymbols.HasValue)
        {
            foreach (var target in tools.Targets ?? Array.Empty<PowerForgeToolReleaseTarget>())
                target.KeepSymbols = request.KeepSymbols.Value;
        }
    }

    private static void ApplyDotNetPublishSkipFlags(DotNetPublishPlan plan, bool skipRestore, bool skipBuild)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        if (skipRestore)
        {
            plan.Restore = false;
            plan.NoRestoreInPublish = true;
            steps = steps.Where(step => step.Kind != DotNetPublishStepKind.Restore).ToArray();
        }

        if (skipBuild)
        {
            plan.Build = false;
            plan.NoBuildInPublish = true;
            steps = steps.Where(step => step.Kind != DotNetPublishStepKind.Build).ToArray();
        }

        plan.Steps = steps;
    }

    private static bool HasSigningOverrides(PowerForgeReleaseRequest request)
    {
        return request.EnableSigning == true
            || !string.IsNullOrWhiteSpace(request.SignProfile)
            || HasExplicitSigningValueOverrides(request);
    }

    private static bool HasExplicitSigningValueOverrides(PowerForgeReleaseRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.SignToolPath)
            || !string.IsNullOrWhiteSpace(request.SignThumbprint)
            || !string.IsNullOrWhiteSpace(request.SignSubjectName)
            || !string.IsNullOrWhiteSpace(request.SignTimestampUrl)
            || !string.IsNullOrWhiteSpace(request.SignDescription)
            || !string.IsNullOrWhiteSpace(request.SignUrl)
            || !string.IsNullOrWhiteSpace(request.SignCsp)
            || !string.IsNullOrWhiteSpace(request.SignKeyContainer);
    }

    private static DotNetPublishSignOptions ApplySigningOverrides(
        DotNetPublishSignOptions? existing,
        PowerForgeReleaseRequest request)
    {
        var sign = existing is null
            ? new DotNetPublishSignOptions()
            : DotNetPublishSigningProfileResolver.CloneSignOptions(existing)!;

        if (request.EnableSigning.HasValue)
            sign.Enabled = request.EnableSigning.Value;
        else if (HasExplicitSigningValueOverrides(request))
            sign.Enabled = true;

        if (!string.IsNullOrWhiteSpace(request.SignToolPath))
            sign.ToolPath = request.SignToolPath!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignThumbprint))
            sign.Thumbprint = request.SignThumbprint!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignSubjectName))
            sign.SubjectName = request.SignSubjectName!.Trim();
        if (request.SignOnMissingTool.HasValue)
            sign.OnMissingTool = request.SignOnMissingTool.Value;
        if (request.SignOnFailure.HasValue)
            sign.OnSignFailure = request.SignOnFailure.Value;
        if (!string.IsNullOrWhiteSpace(request.SignTimestampUrl))
            sign.TimestampUrl = request.SignTimestampUrl!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignDescription))
            sign.Description = request.SignDescription!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignUrl))
            sign.Url = request.SignUrl!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignCsp))
            sign.Csp = request.SignCsp!.Trim();
        if (!string.IsNullOrWhiteSpace(request.SignKeyContainer))
            sign.KeyContainer = request.SignKeyContainer!.Trim();

        return sign;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            builder.Append(value.ToString("x2"));

        return builder.ToString();
    }

    private static string GetRelativePathCompat(string relativeTo, string path)
    {
#if NET472
        var baseUri = new Uri(AppendDirectorySeparator(relativeTo), UriKind.Absolute);
        var targetUri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        var relativeUri = baseUri.MakeRelativeUri(targetUri);
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    private static string AppendDirectorySeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath + Path.DirectorySeparatorChar;
    }

    private static string BuildModuleFailureMessage(string scriptPath, ModuleBuildHostExecutionResult result)
    {
        var message = string.Join(
            Environment.NewLine,
            new[]
            {
                result.StandardError,
                result.StandardOutput
            }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(message))
            return $"Module release workflow failed while executing '{scriptPath}'.";

        return $"Module release workflow failed while executing '{scriptPath}'.{Environment.NewLine}{message}";
    }
}
