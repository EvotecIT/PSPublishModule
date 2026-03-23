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
    private readonly Func<DotNetPublishSpec, string, PowerForgeReleaseRequest, DotNetPublishPlan> _planDotNetTools;
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
            (spec, configPath, request) => PlanDotNetTools(logger, spec, configPath, request),
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
            (spec, configPath, request) => PlanDotNetTools(logger, spec, configPath, request),
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
        Func<DotNetPublishSpec, string, PowerForgeReleaseRequest, DotNetPublishPlan> planDotNetTools,
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
        var runPackages = !request.ToolsOnly && spec.Packages is not null;
        var runTools = !request.PackagesOnly && spec.Tools is not null;
        var configurationOverride = NormalizeConfiguration(request.Configuration);
        var runWorkspaceValidation = spec.WorkspaceValidation is not null && !request.SkipWorkspaceValidation;

        if (!runPackages && !runTools && !runWorkspaceValidation)
        {
            return new PowerForgeReleaseResult
            {
                Success = false,
                ConfigPath = configPath,
                ErrorMessage = "Release config does not enable any selected WorkspaceValidation, Packages, or Tools sections."
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

        if (runPackages)
        {
            ApplyPackageRequestOverrides(spec.Packages!, request, configurationOverride);
            var packageRequest = new ProjectBuildHostRequest
            {
                ConfigPath = configPath,
                ExecuteBuild = !request.PlanOnly && !request.ValidateOnly,
                PlanOnly = request.PlanOnly || request.ValidateOnly ? true : null,
                PublishNuget = request.PublishNuget,
                PublishGitHub = request.PublishProjectGitHub
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

        if (runTools)
        {
            ApplyToolRequestOverrides(spec.Tools!, request, configurationOverride);
            if (UsesDotNetToolWorkflow(spec.Tools!))
            {
                var (dotNetSpec, dotNetSourcePath) = _loadDotNetToolsSpec(spec.Tools!, configPath);
                var dotNetPlan = _planDotNetTools(dotNetSpec, dotNetSourcePath, request);
                ApplyDotNetPublishSkipFlags(dotNetPlan, request.SkipRestore, request.SkipBuild);
                result.DotNetToolPlan = dotNetPlan;

                if (!request.PlanOnly && !request.ValidateOnly)
                {
                    var dotNetTools = _runDotNetTools(dotNetPlan);
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
                        var releases = PublishDotNetToolGitHubReleases(spec, configDirectory, dotNetPlan, dotNetTools);
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
            PopulateReleaseOutputs(spec, request, configDirectory, result);

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
        var spec = JsonSerializer.Deserialize<WorkspaceValidationSpec>(json, CreateWorkspaceValidationJsonOptions());
        if (spec is null)
            throw new InvalidOperationException($"Unable to deserialize workspace validation config: {fullPath}");

        return (spec, fullPath);
    }

    private static JsonSerializerOptions CreateWorkspaceValidationJsonOptions()
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

    private void PopulateReleaseOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory,
        PowerForgeReleaseResult result)
    {
        var assetEntries = CollectReleaseAssetEntries(result)
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var assets = assetEntries
            .Select(entry => entry.Path)
            .ToArray();

        result.ReleaseAssetEntries = assetEntries;
        result.ReleaseAssets = assets;

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
            return;

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
            var resolvedManifestPath = manifestPath!;
            var manifest = new
            {
                schemaVersion = 1,
                createdUtc = DateTime.UtcNow.ToString("o"),
                configPath = result.ConfigPath,
                assets,
                assetEntries = assetEntries.Select(entry => new
                {
                    entry.Path,
                    category = entry.Category.ToString(),
                    entry.Source,
                    entry.Target,
                    entry.RelativeStagePath,
                    entry.StagedPath
                }).ToArray(),
                packages = BuildPackageManifestSection(result.Packages),
                legacyTools = BuildLegacyToolsManifestSection(result.Tools),
                dotNetTools = BuildDotNetToolsManifestSection(result.DotNetTools),
                githubReleases = result.ToolGitHubReleases
            };

            Directory.CreateDirectory(Path.GetDirectoryName(resolvedManifestPath)!);
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            File.WriteAllText(resolvedManifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            result.ReleaseManifestPath = resolvedManifestPath;
        }

        if (!string.IsNullOrWhiteSpace(checksumsPath))
        {
            var resolvedChecksumsPath = checksumsPath!;
            var checksumInputs = new List<string>(assets);
            var releaseManifestPath = result.ReleaseManifestPath;
            if (!string.IsNullOrWhiteSpace(releaseManifestPath) && File.Exists(releaseManifestPath))
                checksumInputs.Add(releaseManifestPath!);

            var uniqueChecksumInputs = checksumInputs
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(resolvedChecksumsPath)!);
            var lines = uniqueChecksumInputs
                .Select(path => $"{ComputeSha256(path!)} *{GetRelativePathCompat(configDirectory, path!).Replace('\\', '/')}")
                .ToArray();
            File.WriteAllLines(resolvedChecksumsPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            result.ReleaseChecksumsPath = resolvedChecksumsPath;
        }

        if (!string.IsNullOrWhiteSpace(stageRootTemplate))
        {
            var stageRoot = ResolveOutputPath(configDirectory, stageRootTemplate!);
            result.ReleaseAssetEntries = StageReleaseAssets(assetEntries, stageRoot, outputs.Staging).ToArray();
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
        DotNetPublishResult result)
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
            var version = ResolveDotNetTargetVersion(target, result);
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

    private static string? ResolveDotNetTargetVersion(DotNetPublishTargetPlan target, DotNetPublishResult result)
    {
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

        return string.IsNullOrWhiteSpace(msiVersion) ? null : msiVersion;
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
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, options)
            ?? throw new InvalidOperationException($"DotNet publish config could not be deserialized: {configPath}");

        if (!string.IsNullOrWhiteSpace(tools.DotNetPublishProfile))
            spec.Profile = tools.DotNetPublishProfile!.Trim();

        return (spec, configPath);
    }

    private static DotNetPublishPlan PlanDotNetTools(
        ILogger logger,
        DotNetPublishSpec spec,
        string configPath,
        PowerForgeReleaseRequest request)
    {
        if (request.Flavors is { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Release --flavor overrides are only supported by legacy Tools.Targets workflows. " +
                "Use DotNet publish Styles in config when Tools uses DotNetPublish.");
        }

        ApplyDotNetRequestOverrides(spec, request);
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

    private static PowerForgeReleaseAssetEntry[] CollectReleaseAssetEntries(PowerForgeReleaseResult result)
    {
        var assets = new List<PowerForgeReleaseAssetEntry>();

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
            .SelectMany(CreateDotNetArtefactEntries));

        assets.AddRange(
            (result.DotNetTools?.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
            .SelectMany(build => build.OutputFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Installer,
                Source = "DotNetPublish",
                Target = (result.DotNetTools?.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
                    .FirstOrDefault(build => (build.OutputFiles ?? Array.Empty<string>()).Contains(path!, StringComparer.OrdinalIgnoreCase))
                    ?.Target
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
                Target = project.ProjectName
            };
        }

        if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath) && File.Exists(project.ReleaseZipPath))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = project.ReleaseZipPath!,
                Category = PowerForgeReleaseAssetCategory.Package,
                Source = "Packages",
                Target = project.ProjectName
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
                Target = artifact.Target
            };
        }
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateDotNetArtefactEntries(DotNetPublishArtefactResult artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.ZipPath) || !File.Exists(artifact.ZipPath))
            yield break;

        yield return new PowerForgeReleaseAssetEntry
        {
            Path = artifact.ZipPath!,
            Category = artifact.Category == DotNetPublishArtefactCategory.Bundle
                ? PowerForgeReleaseAssetCategory.Portable
                : PowerForgeReleaseAssetCategory.Tool,
            Source = "DotNetPublish",
            Target = artifact.Target
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
                Target = storePackage.Target
            };
        }
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
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                continue;

            var categoryDirectory = ResolveStageDirectory(options, entry.Category);
            var relativeStagePath = Path.Combine(categoryDirectory, Path.GetFileName(sourcePath));
            var destinationPath = Path.Combine(stageRoot, relativeStagePath);
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var destinationFullPath = Path.GetFullPath(destinationPath);

            entry.RelativeStagePath = relativeStagePath.Replace('\\', '/');
            entry.StagedPath = destinationFullPath;

            if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
            File.Copy(sourceFullPath, destinationFullPath, overwrite: true);
        }

        return assetEntries;
    }

    private static string ResolveStageDirectory(PowerForgeReleaseStagingOptions options, PowerForgeReleaseAssetCategory category)
    {
        return category switch
        {
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
}
