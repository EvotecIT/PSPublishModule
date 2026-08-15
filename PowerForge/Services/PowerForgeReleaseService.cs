using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Orchestrates package and tool release workflows from one unified configuration.
/// </summary>
internal sealed partial class PowerForgeReleaseService
{
    private static readonly JsonSerializerOptions DotNetToolsJsonOptions = CreateJsonOptions();
    private static readonly JsonSerializerOptions WorkspaceValidationJsonOptions = CreateJsonOptions();
    private static readonly Regex AnsiEscapeSequence = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

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

    private const string DefaultDotNetRunReportMarkdownTemplate =
        "Artifacts/DotNetPublish/run-report.md";

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
    private readonly Func<PowerForgeToolReleasePlan, IPowerForgeReleaseProgressReporterV2?, CancellationToken, PowerForgeToolReleaseResult> _runTools;
    private readonly Func<PowerForgeToolReleaseSpec, string, (DotNetPublishSpec Spec, string SourceConfigPath)> _loadDotNetToolsSpec;
    private readonly Func<DotNetPublishSpec, string, PowerForgeReleaseRequest, ISet<PowerForgeReleaseToolOutputKind>, DotNetPublishPlan> _planDotNetTools;
    private readonly Func<DotNetPublishPlan, IDotNetPublishProgressReporter?, CancellationToken, DotNetPublishResult> _runDotNetTools;
    private readonly Func<GitHubReleasePublishRequest, CancellationToken, GitHubReleasePublishResult> _publishGitHubRelease;
    private readonly Func<string, string, IEnumerable<string>, CancellationToken, string[]>? _restorePublishedNuGetAssets;
    private readonly Func<string, string, string, IEnumerable<string>, CancellationToken, string[]>? _restorePublishedModuleAssets;
    private readonly Func<string, string, string, string, GitHubReleaseVersionOccupancy> _probeGitHubReleaseVersion;
    private readonly Func<PowerForgeWingetSubmissionPlan, PowerForgeWingetSubmissionResult> _submitWinget;
    private readonly Func<AppleAppArchiveRequest, AppleAppArchiveResult> _archiveAppleApp;
    private readonly Func<AppleAppArchiveUploadRequest, AppleAppArchiveUploadResult> _uploadAppleApp;
    private readonly Func<AppStoreConnectReleasePreparationRequest, AppStoreConnectReleasePreparationResult> _prepareAppleDistribution;
    private readonly Func<AppStoreConnectTestFlightDistributionRequest, AppStoreConnectTestFlightDistributionResult> _distributeTestFlight;
    private readonly Func<AppStoreConnectBetaAppReviewSubmissionRequest, AppStoreConnectBetaAppReviewSubmissionResult> _submitTestFlightBetaReview;
    private readonly Func<AppStoreConnectReviewSubmissionRequest, AppStoreConnectReviewSubmissionResult> _submitAppleReview;
    private readonly Func<AppStoreConnectApiCredential, AppStoreConnectReleaseReadinessRequest, AppStoreConnectReleaseReadinessResult> _checkAppleReleaseReadiness;
    private readonly Func<AppStoreConnectVersionReleaseRequest, AppStoreConnectVersionReleaseResult> _releaseAppleVersion;
    private readonly Func<AppStoreConnectReleaseStateRequest, AppStoreConnectReleaseStateResult> _getAppleReleaseState;
    private readonly Func<AppStoreConnectApiCredential, string, AppStoreConnectBuildUploadInfo?> _getAppleBuildUpload;
    private readonly Func<AppStoreConnectApiCredential, string, ApplePlatform, long> _getHighestAppleBuildNumber;
    private readonly Func<AppStoreConnectApiCredential, string, ApplePlatform, PowerForgeAppleRemoteVersionInventory> _getAppleVersionInventory;
    private readonly Func<AppStoreConnectApiCredential, string, AppStoreConnectAppInfo[]> _findAppleApps;
    private readonly Func<AppStoreConnectApiCredential, AppStoreConnectGovernanceSpec, AppStoreConnectGovernancePlan> _planAppleGovernance;
    private readonly Func<AppleNotarizationRequest, AppleNotarizationResult> _notarizeAppleArtifact;
    private readonly Func<PowerForgeAppleAppReleaseTargetPlan, bool> _generateAppleProject;
    private readonly Action<TimeSpan> _delay;
    private readonly AppleReleaseArtifactService _appleArtifactService;
    private readonly AppleReleaseReceiptStore _appleReceiptStore;
    private readonly Func<VirusTotalMonitorPublishRequest, CancellationToken, VirusTotalMonitorPublishResult> _publishVirusTotalMonitor;

    /// <summary>
    /// Creates a new unified release service.
    /// </summary>
    public PowerForgeReleaseService(ILogger logger)
        : this(logger, signAssemblies: null, validateAssemblySigning: null)
    {
    }

    /// <summary>
    /// Creates a new unified release service with optional package assembly signing callbacks.
    /// </summary>
    public PowerForgeReleaseService(
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning)
        : this(
            logger,
            (request, config, configPath) => new ProjectBuildHostService(logger, signAssemblies, validateAssemblySigning).Execute(request, config, configPath),
            (spec, configPath, request) => new PowerForgeToolReleaseService(logger).Plan(spec, configPath, request),
            plan => new PowerForgeToolReleaseService(logger).Run(plan),
            LoadDotNetToolsSpec,
            (spec, configPath, request, selectedOutputs) => PlanDotNetTools(logger, spec, configPath, request, selectedOutputs),
            plan => new DotNetPublishPipelineRunner(logger).Run(plan, progress: null),
            publishRequest => new GitHubReleasePublisher(logger).PublishRelease(publishRequest),
            plan => new WingetSubmissionService(logger).Run(plan),
            request => new AppleAppArchiveService().CreateArchiveAsync(request).GetAwaiter().GetResult(),
            request => new AppleAppArchiveService().UploadArchiveAsync(request).GetAwaiter().GetResult(),
            null,
            runToolsWithProgress: (plan, progress) =>
                new PowerForgeToolReleaseService(logger).Run(plan, progress),
            runToolsWithProgressAndCancellation: (plan, progress, cancellationToken) =>
                new PowerForgeToolReleaseService(logger).Run(plan, progress, cancellationToken),
            runDotNetToolsWithProgressAndCancellation: (plan, progress, cancellationToken) =>
                new DotNetPublishPipelineRunner(logger).Run(plan, progress, cancellationToken),
            publishGitHubReleaseWithCancellation: (publishRequest, cancellationToken) =>
                new GitHubReleasePublisher(logger).PublishRelease(publishRequest, cancellationToken))
    {
    }

    internal PowerForgeReleaseService(
        ILogger logger,
        Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> executePackages,
        Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools,
        Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> runTools,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> publishGitHubRelease,
        Func<string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedNuGetAssets = null,
        Func<string, string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedModuleAssets = null)
        : this(
            logger,
            executePackages,
            planTools,
            runTools,
            LoadDotNetToolsSpec,
            (spec, configPath, request, selectedOutputs) => PlanDotNetTools(logger, spec, configPath, request, selectedOutputs),
            plan => new DotNetPublishPipelineRunner(logger).Run(plan, progress: null),
            publishGitHubRelease,
            plan => new WingetSubmissionService(logger).Run(plan),
            request => new AppleAppArchiveService().CreateArchiveAsync(request).GetAwaiter().GetResult(),
            request => new AppleAppArchiveService().UploadArchiveAsync(request).GetAwaiter().GetResult(),
            null,
            restorePublishedNuGetAssets: restorePublishedNuGetAssets,
            restorePublishedModuleAssets: restorePublishedModuleAssets)
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
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> publishGitHubRelease,
        Func<PowerForgeWingetSubmissionPlan, PowerForgeWingetSubmissionResult>? submitWinget = null,
        Func<AppleAppArchiveRequest, AppleAppArchiveResult>? archiveAppleApp = null,
        Func<AppleAppArchiveUploadRequest, AppleAppArchiveUploadResult>? uploadAppleApp = null,
        Func<AppStoreConnectReleasePreparationRequest, AppStoreConnectReleasePreparationResult>? prepareAppleDistribution = null,
        Func<AppStoreConnectTestFlightDistributionRequest, AppStoreConnectTestFlightDistributionResult>? distributeTestFlight = null,
        Func<AppStoreConnectBetaAppReviewSubmissionRequest, AppStoreConnectBetaAppReviewSubmissionResult>? submitTestFlightBetaReview = null,
        Func<AppStoreConnectReviewSubmissionRequest, AppStoreConnectReviewSubmissionResult>? submitAppleReview = null,
        Func<AppStoreConnectVersionReleaseRequest, AppStoreConnectVersionReleaseResult>? releaseAppleVersion = null,
        Func<AppStoreConnectReleaseStateRequest, AppStoreConnectReleaseStateResult>? getAppleReleaseState = null,
        Func<AppStoreConnectApiCredential, string, AppStoreConnectBuildUploadInfo?>? getAppleBuildUpload = null,
        Func<PowerForgeAppleAppReleaseTargetPlan, bool>? generateAppleProject = null,
        Action<TimeSpan>? delay = null,
        AppleReleaseArtifactService? appleArtifactService = null,
        AppleReleaseReceiptStore? appleReceiptStore = null,
        Func<PowerForgeToolReleasePlan, IPowerForgeReleaseProgressReporterV2?, PowerForgeToolReleaseResult>? runToolsWithProgress = null,
        Func<DotNetPublishPlan, IDotNetPublishProgressReporter?, DotNetPublishResult>? runDotNetToolsWithProgress = null,
        Func<DotNetPublishPlan, IDotNetPublishProgressReporter?, CancellationToken, DotNetPublishResult>? runDotNetToolsWithProgressAndCancellation = null,
        Func<PowerForgeToolReleasePlan, IPowerForgeReleaseProgressReporterV2?, CancellationToken, PowerForgeToolReleaseResult>? runToolsWithProgressAndCancellation = null,
        Func<ModuleBuildHostBuildRequest, CancellationToken, ModuleBuildHostExecutionResult>? executeModuleBuild = null,
        Func<GitHubReleasePublishRequest, CancellationToken, GitHubReleasePublishResult>? publishGitHubReleaseWithCancellation = null,
        Func<string, string, string, string, GitHubReleaseVersionOccupancy>? probeGitHubReleaseVersion = null,
        Func<AppStoreConnectApiCredential, string, ApplePlatform, long>? getHighestAppleBuildNumber = null,
        Func<AppStoreConnectApiCredential, string, ApplePlatform, PowerForgeAppleRemoteVersionInventory>? getAppleVersionInventory = null,
        Func<AppStoreConnectApiCredential, string, AppStoreConnectAppInfo[]>? findAppleApps = null,
        Func<AppleNotarizationRequest, AppleNotarizationResult>? notarizeAppleArtifact = null,
        Func<AppStoreConnectApiCredential, AppStoreConnectGovernanceSpec, AppStoreConnectGovernancePlan>? planAppleGovernance = null,
        Func<AppStoreConnectApiCredential, AppStoreConnectReleaseReadinessRequest, AppStoreConnectReleaseReadinessResult>? checkAppleReleaseReadiness = null,
        Func<string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedNuGetAssets = null,
        Func<string, string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedModuleAssets = null,
        Func<VirusTotalMonitorPublishRequest, CancellationToken, VirusTotalMonitorPublishResult>? publishVirusTotalMonitor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executePackages = executePackages ?? throw new ArgumentNullException(nameof(executePackages));
        _planTools = planTools ?? throw new ArgumentNullException(nameof(planTools));
        if (runTools is null)
            throw new ArgumentNullException(nameof(runTools));
        _runTools = runToolsWithProgressAndCancellation
            ?? (runToolsWithProgress is not null
                ? (plan, progress, _) => runToolsWithProgress(plan, progress)
                : (plan, _, _) => runTools(plan));
        _loadDotNetToolsSpec = loadDotNetToolsSpec ?? throw new ArgumentNullException(nameof(loadDotNetToolsSpec));
        _planDotNetTools = planDotNetTools ?? throw new ArgumentNullException(nameof(planDotNetTools));
        if (runDotNetTools is null)
            throw new ArgumentNullException(nameof(runDotNetTools));
        _runDotNetTools = runDotNetToolsWithProgressAndCancellation
            ?? (runDotNetToolsWithProgress is not null
                ? (plan, progress, _) => runDotNetToolsWithProgress(plan, progress)
                : (plan, _, _) => runDotNetTools(plan));
        if (publishGitHubRelease is null)
            throw new ArgumentNullException(nameof(publishGitHubRelease));
        _publishGitHubRelease = publishGitHubReleaseWithCancellation
            ?? ((publishRequest, _) => publishGitHubRelease(publishRequest));
        _restorePublishedNuGetAssets = restorePublishedNuGetAssets;
        _restorePublishedModuleAssets = restorePublishedModuleAssets;
        _probeGitHubReleaseVersion = probeGitHubReleaseVersion
            ?? GitHubReleaseVersionAvailabilityService.Probe;
        _submitWinget = submitWinget ?? (plan => new WingetSubmissionService(logger).Run(plan));
        _archiveAppleApp = archiveAppleApp ?? (request => new AppleAppArchiveService().CreateArchiveAsync(request).GetAwaiter().GetResult());
        _uploadAppleApp = uploadAppleApp ?? (request => new AppleAppArchiveService().UploadArchiveAsync(request).GetAwaiter().GetResult());
        _prepareAppleDistribution = prepareAppleDistribution ?? PrepareAppleDistribution;
        _distributeTestFlight = distributeTestFlight ?? DistributeTestFlight;
        _submitTestFlightBetaReview = submitTestFlightBetaReview ?? SubmitTestFlightBetaReview;
        _submitAppleReview = submitAppleReview ?? SubmitAppleReview;
        _checkAppleReleaseReadiness = checkAppleReleaseReadiness ?? CheckAppleReleaseReadiness;
        _releaseAppleVersion = releaseAppleVersion ?? ReleaseAppleVersion;
        _getAppleReleaseState = getAppleReleaseState ?? GetAppleReleaseState;
        _getAppleBuildUpload = getAppleBuildUpload ?? GetAppleBuildUpload;
        _getHighestAppleBuildNumber = getHighestAppleBuildNumber ?? GetHighestAppleBuildNumber;
        _getAppleVersionInventory = getAppleVersionInventory ?? GetAppleVersionInventory;
        _findAppleApps = findAppleApps ?? FindAppleApps;
        _planAppleGovernance = planAppleGovernance ?? PlanAppleGovernance;
        _notarizeAppleArtifact = notarizeAppleArtifact ?? (request =>
            new AppleNotarizationService().NotarizeAsync(request).GetAwaiter().GetResult());
        _generateAppleProject = generateAppleProject ?? (app => new AppleProjectGenerationService().Generate(app));
        _delay = delay ?? Thread.Sleep;
        _appleArtifactService = appleArtifactService ?? new AppleReleaseArtifactService();
        _appleReceiptStore = appleReceiptStore ?? new AppleReleaseReceiptStore();
        _executeModuleBuild = executeModuleBuild
            ?? ((moduleRequest, cancellationToken) => new ModuleBuildHostService()
                .ExecuteBuildAsync(moduleRequest, cancellationToken)
                .GetAwaiter()
                .GetResult());
        _publishVirusTotalMonitor = publishVirusTotalMonitor
            ?? ((publishRequest, cancellationToken) => new VirusTotalMonitorPublisher()
                .PublishAsync(publishRequest, cancellationToken)
                .GetAwaiter()
                .GetResult());
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

        using var deferredModuleStaging = new DeferredModuleStagingDirectory(_logger);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required.", nameof(request));

        var configPath = Path.GetFullPath(request.ConfigPath.Trim().Trim('"'));
        if (File.Exists(configPath))
            PowerForgeReleaseConfigurationSecretValidator.ValidateJson(File.ReadAllText(configPath));
        PowerForgeReleaseConfigurationSecretValidator.Validate(spec);
        request.LoadedConfigurationSha256 = null;
        string expectedLoadedConfigurationPath = string.IsNullOrWhiteSpace(request.EffectiveConfigurationPath)
            ? configPath
            : Path.GetFullPath(request.EffectiveConfigurationPath!.Trim().Trim('"'));
        if (!AppleReleasePathsEqual(expectedLoadedConfigurationPath, configPath))
        {
            PowerForgeReleaseConfigurationSecretValidator.ValidateJson(
                File.ReadAllText(expectedLoadedConfigurationPath));
        }
        if (!string.IsNullOrWhiteSpace(request.EffectiveConfigurationPath) &&
            (string.IsNullOrWhiteSpace(spec.LoadedConfigurationPath) ||
             !AppleReleasePathsEqual(Path.GetFullPath(spec.LoadedConfigurationPath!), expectedLoadedConfigurationPath)))
        {
            throw new InvalidOperationException(
                "Generated configuration attestation must use the exact authorized configuration loaded by the release engine.");
        }
        if (!string.IsNullOrWhiteSpace(spec.LoadedConfigurationPath) &&
            AppleReleasePathsEqual(Path.GetFullPath(spec.LoadedConfigurationPath!), expectedLoadedConfigurationPath))
        {
            request.LoadedConfigurationSha256 = spec.LoadedConfigurationSha256;
        }
        if (spec.GitHub is { Publish: true } configuredGitHub &&
            request.PublishProjectGitHub != false &&
            IsVerifiedGitHubRecoveryRequested(configuredGitHub))
        {
            ValidateVerifiedGitHubRecoveryConfiguration(configuredGitHub);
        }
        var registryPublishingSkippedForVerifiedGitHubRecovery =
            ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request);
        ApplyAppleAction(spec.AppleApps, request);
        var explicitAppleAction = request.AppleAction != PowerForgeAppleReleaseAction.Configured;
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        ValidateVirusTotalConfiguration(spec.VirusTotal);
        var selectedToolOutputs = ResolveSelectedToolOutputs(request);
        var selectedTargets = NormalizeStrings(request.Targets);
        var runModule = !explicitAppleAction &&
                        !request.AppleOnly &&
                        spec.Module is not null &&
                        (!request.PackagesOnly && !request.ToolsOnly || request.ModuleOnly);
        var runPackages = !explicitAppleAction &&
                          !request.AppleOnly &&
                          spec.Packages is not null &&
                          !request.ModuleOnly &&
                          !request.ToolsOnly;
        var runTools = !explicitAppleAction &&
                       !request.AppleOnly &&
                       spec.Tools is not null &&
                       !request.ModuleOnly &&
                       !request.PackagesOnly;
        var runAppleApps = spec.AppleApps is not null &&
                           !request.SkipAppleApps &&
                           !request.ModuleOnly &&
                           !request.PackagesOnly &&
                           !request.ToolsOnly;
        ApplyExplicitToolOnlyReleaseVersion(request, runModule, runPackages, runTools, runAppleApps);
        var appleTargetMatches = runAppleApps
            ? ResolveAppleTargetMatches(spec.AppleApps!, selectedTargets)
            : Array.Empty<string>();
        var toolTargetMatches = Array.Empty<string>();
        var runWorkspaceValidation = !explicitAppleAction &&
                                     !request.AppleOnly &&
                                     spec.WorkspaceValidation is not null &&
                                     !request.SkipWorkspaceValidation;
        DotNetPublishSpec? dotNetSpecForTools = null;
        string? dotNetSourcePathForTools = null;

        if (runTools)
        {
            if (UsesDotNetToolWorkflow(spec.Tools!))
            {
                ApplyDotNetPublishProfileOverride(spec.Tools!);
                var inlineMatches = ResolveOptionalDotNetToolTargetMatches(spec.Tools!.DotNetPublish, selectedTargets);
                var selectedTargetsAreAppleOnly = runAppleApps &&
                                                  selectedTargets.Length > 0 &&
                                                  appleTargetMatches.Length == selectedTargets.Length &&
                                                  inlineMatches.Length == 0;
                var externalConfigExists = DotNetToolsConfigExists(spec.Tools!, expectedLoadedConfigurationPath);
                var appleTargetsNeedDotNetDisambiguation = selectedTargetsAreAppleOnly &&
                                                           AppleTargetSelectionUsesNameOrScheme(spec.AppleApps!, selectedTargets);
                var shouldLoadDotNetSpec = selectedTargets.Length == 0 ||
                                           inlineMatches.Length > 0 ||
                                           !selectedTargetsAreAppleOnly ||
                                           externalConfigExists && appleTargetsNeedDotNetDisambiguation;
                if (shouldLoadDotNetSpec)
                {
                    var dotNetSource = _loadDotNetToolsSpec(spec.Tools!, expectedLoadedConfigurationPath);
                    dotNetSpecForTools = dotNetSource.Spec;
                    dotNetSourcePathForTools = ResolveDotNetPlanningConfigurationPath(
                        dotNetSource.SourceConfigPath,
                        configPath,
                        spec.Tools!.DotNetPublish is not null);
                    toolTargetMatches = ResolveDotNetToolTargetMatches(dotNetSpecForTools, selectedTargets);
                }
                else
                {
                    toolTargetMatches = inlineMatches;
                }
            }
            else
            {
                toolTargetMatches = ResolveLegacyToolTargetMatches(spec.Tools!, selectedTargets);
            }
        }

        ValidateSelectedTargets(selectedTargets, toolTargetMatches, appleTargetMatches, runTools, runAppleApps);
        var hasTargetAwareSelection = selectedTargets.Length > 0 &&
                                      (toolTargetMatches.Length > 0 || appleTargetMatches.Length > 0);
        if (hasTargetAwareSelection)
        {
            runModule = false;
            runPackages = false;
        }
        else if (runModule &&
                 spec.Module!.IncludesPackages &&
                 (!request.PlanOnly && !request.ValidateOnly || request.ModuleOnly))
        {
            runPackages = false;
        }
        request.GeneratedProvenancePaths = ResolveGeneratedModuleProvenancePaths(
            spec.Module,
            configPath,
            request,
            runModule);
        var publishUnifiedGitHub = !explicitAppleAction &&
                                   ShouldPublishUnifiedGitHub(spec, request, runModule);

        ValidateVersionCoordinationConfiguration(spec, runModule);

        var willRunTools = runTools && ShouldRunSectionForTargets(selectedTargets, toolTargetMatches, runAppleApps, appleTargetMatches);
        var willRunAppleApps = runAppleApps && ShouldRunSectionForTargets(selectedTargets, appleTargetMatches, runTools, toolTargetMatches);
        var hasNonAppleConfigurationConsumer = runWorkspaceValidation || runModule || runPackages || willRunTools;
        var configurationOverride = hasNonAppleConfigurationConsumer
            ? NormalizeConfiguration(request.Configuration)
            : null;
        var appleConfigurationOverride = NormalizeAppleConfiguration(request.Configuration);

        if (willRunTools)
            ApplyToolRequestOverrides(spec.Tools!, request, configurationOverride);

        var publishVirusTotalMonitor = ShouldPublishVirusTotalMonitor(
            spec,
            request,
            explicitAppleAction,
            runModule,
            runPackages,
            willRunTools,
            publishUnifiedGitHub);
        var virusTotalApiKey = ResolveVirusTotalApiKeyForExecution(
            spec.VirusTotal,
            configDirectory,
            planOrValidation: !publishVirusTotalMonitor);
        if (publishVirusTotalMonitor)
        {
            var virusTotalProject = ResolveVirusTotalProjectName(
                spec,
                spec.VirusTotal!,
                configDirectory);
            using (AcquireVirusTotalReceiptLock(spec.VirusTotal!, configDirectory))
                EnsureVirusTotalReceiptWritable(spec.VirusTotal!, configDirectory, virusTotalProject);
        }
        var captureModuleArtifactProvenance = ShouldCaptureVirusTotalModuleArtifactProvenance(
            spec,
            request,
            runModule);

        if (!runModule && !runPackages && !runTools && !runAppleApps && !runWorkspaceValidation)
        {
            return new PowerForgeReleaseResult
            {
                Success = false,
                ConfigPath = configPath,
                ErrorMessage = "Release config does not enable any selected WorkspaceValidation, Module, Packages, Tools, or AppleApps sections."
            };
        }

        var result = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = configPath,
            RegistryPublishingSkippedForVerifiedGitHubRecovery =
                registryPublishingSkippedForVerifiedGitHubRecovery
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

        var coordinateModuleAndPackageVersions = ShouldCoordinateModuleAndPackageVersions(spec, runModule, runPackages);
        ModuleBuildHostBuildRequest? deferredModulePublishRequest = null;
        IReadOnlyDictionary<string, ModuleArtifactSnapshot>? moduleArtifactBaseline = null;
        if (coordinateModuleAndPackageVersions)
        {
            var versionFloor = ResolveCoordinatedModuleVersionFloor(spec.Module!, configPath, request);
            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.Versioning,
                CountConfiguredPackageProjects(spec.Packages!),
                $"Resolving shared version from {spec.Module!.VersionPrimaryProject}");
            var coordination = ResolveCoordinatedReleaseVersion(
                spec,
                configPath,
                request,
                configurationOverride,
                publishUnifiedGitHub,
                versionFloor);
            result.Packages = coordination.Packages;
            if (!coordination.Packages.Success)
            {
                request.Progress?.PhaseFailed(
                    PowerForgeReleaseProgressPhase.Versioning,
                    coordination.Packages.ErrorMessage);
                result.Success = false;
                result.ErrorMessage = coordination.Packages.ErrorMessage ?? "Package release workflow failed.";
                return result;
            }

            request.ResolvedReleaseVersion = coordination.Version;
            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.Versioning,
                $"Shared version {request.ResolvedReleaseVersion}");
        }

        if (runModule)
        {
            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.Module,
                1,
                request.PlanOnly || request.ValidateOnly ? "Planning module build" : "Building PowerShell module");
            var packagePublishingRequested =
                !request.ModuleOnly &&
                ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
                 (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true);
            var module = PrepareModuleRelease(
                spec.Module!,
                configPath,
                request,
                configurationOverride,
                packagePublishingRequested,
                publishUnifiedGitHub);
            result.ModulePlan = module.Plan;
            result.ModuleAssets = module.ArtifactPaths;
            if (captureModuleArtifactProvenance)
                moduleArtifactBaseline = CaptureModuleArtifactBaseline(result.ModuleAssets);
            var deferModulePublishing = ShouldDeferModulePublishing(
                module.Request,
                request,
                runPackages,
                willRunTools,
                willRunAppleApps);
            if (deferModulePublishing)
            {
                if (!request.PlanOnly &&
                    !request.ValidateOnly &&
                    string.IsNullOrWhiteSpace(module.Request.StagingPath))
                {
                    module.Request.StagingPath = deferredModuleStaging.GetOrCreatePath();
                    module.Plan.StagingPath = module.Request.StagingPath;
                }
                module.Request.RequireReusableOutput = module.Request.ScriptPath is not null;
                deferredModulePublishRequest = module.Request;
            }

            if (!request.PlanOnly && !request.ValidateOnly)
            {
                var moduleResult = deferModulePublishing
                    ? ExecuteModuleRequest(
                        module.Request,
                        ConfigurationGateMode.Build,
                        includeModulePublishing: false,
                        cancellationToken: request.CancellationToken)
                    : _executeModuleBuild(module.Request, request.CancellationToken);
                result.Module = moduleResult;
                if (!moduleResult.Succeeded)
                {
                    var moduleSource = module.Request.ConfigPath ?? module.Request.ScriptPath ?? "module build";
                    request.Progress?.PhaseFailed(
                        PowerForgeReleaseProgressPhase.Module,
                        BuildModuleFailureMessage(moduleSource, moduleResult));
                    result.Success = false;
                    result.ErrorMessage = BuildModuleFailureMessage(moduleSource, moduleResult);
                    return result;
                }

                UpdateResolvedModuleVersion(result.ModulePlan, result.ModuleAssets);
                result.ModuleAssets = ExpandModuleArtifactPaths(
                    result.ModuleAssets,
                    result.ModulePlan?.ModuleName,
                    result.ModulePlan?.ModuleVersion,
                    result.ModulePlan?.PreReleaseTag);
                if (result.ModulePlan is not null)
                    result.ModulePlan.ArtifactPaths = result.ModuleAssets;
                if (captureModuleArtifactProvenance)
                {
                    result.ModuleProducedAssets = ResolveProducedModuleArtifacts(
                        result.ModuleAssets,
                        moduleArtifactBaseline);
                }

                if (result.ModulePlan?.IncludesProjectPackages == true &&
                    module.Request.ConfigPath is not null)
                {
                    result.ModulePackagePlans = new ModulePackageReleaseCheckpointService()
                        .Capture(configPath, spec);
                }
            }
            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.Module,
                string.IsNullOrWhiteSpace(result.ModulePlan?.ModuleVersion)
                    ? "Module lane complete"
                    : $"Version {result.ModulePlan!.ModuleVersion}");
        }

        ValidatePostBuildSourceState(request);

        var earlyAppleReleaseVersion = ResolveAppleSharedReleaseVersion(spec, request, result);
        if (willRunAppleApps)
        {
            var selectedAppleTargets = GetSectionSelectedTargets(selectedTargets, appleTargetMatches, runTools, toolTargetMatches);
            _ = PrepareAppleRelease(
                spec.AppleApps!,
                request,
                configPath,
                appleConfigurationOverride,
                earlyAppleReleaseVersion,
                request.SkipBuild,
                selectedAppleTargets,
                allowUnresolvedResolvedVersion: true);
        }

        if (runPackages && result.Packages is null)
        {
            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.Packages,
                CountConfiguredPackageProjects(spec.Packages!),
                request.PlanOnly || request.ValidateOnly ? "Planning package build" : "Building project packages");
            var packages = ExecutePackageRelease(
                spec.Packages!,
                configPath,
                request,
                configurationOverride,
                publishUnifiedGitHub);
            result.Packages = packages;
            if (!packages.Success)
            {
                request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Packages, packages.ErrorMessage);
                result.Success = false;
                result.ErrorMessage = packages.ErrorMessage ?? "Package release workflow failed.";
                return result;
            }
            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.Packages,
                string.IsNullOrWhiteSpace(ResolveSharedReleaseVersion(spec, result))
                    ? "Package lane complete"
                    : $"Version {ResolveSharedReleaseVersion(spec, result)}");
        }

        var sharedReleaseVersion = request.ResolvedReleaseVersion ?? ResolveSharedReleaseVersion(spec, result);
        request.ResolvedReleaseVersion ??= sharedReleaseVersion;

        PowerForgeAppleReleasePlan? applePlan = null;
        if (willRunAppleApps)
        {
            var selectedAppleTargets = GetSectionSelectedTargets(selectedTargets, appleTargetMatches, runTools, toolTargetMatches);
            var appleReleaseVersion = ResolveAppleSharedReleaseVersion(spec, request, result);
            applePlan = PrepareAppleRelease(
                spec.AppleApps!,
                request,
                configPath,
                appleConfigurationOverride,
                appleReleaseVersion,
                request.SkipBuild,
                selectedAppleTargets);
            result.AppleAppPlan = applePlan;
            if (request.PlanOnly)
                result.AppleReceipt = CreateApplePlanReceipt(applePlan);
            if (request.PlanOnly || request.ValidateOnly || request.CheckpointAppleApps)
                ScrubApplePlanCredentials(result.AppleAppPlan);
        }

        if (runTools)
        {
            if (willRunTools)
            {
                request.Progress?.PhaseStarted(
                    PowerForgeReleaseProgressPhase.Tools,
                    CountConfiguredToolOutputs(spec.Tools!, dotNetSpecForTools),
                    request.PlanOnly || request.ValidateOnly ? "Planning executable outputs" : "Publishing executable outputs");
            }
            if (UsesDotNetToolWorkflow(spec.Tools!))
            {
                if (dotNetSpecForTools is not null &&
                    dotNetSourcePathForTools is not null &&
                    willRunTools)
                {
                    var dotNetTargets = GetSectionSelectedTargets(selectedTargets, toolTargetMatches, runAppleApps, appleTargetMatches);
                    var dotNetPlan = WithRequestTargets(
                        request,
                        dotNetTargets,
                        () => _planDotNetTools(dotNetSpecForTools, dotNetSourcePathForTools, request, selectedToolOutputs));
                    ApplySharedReleaseVersion(dotNetPlan, sharedReleaseVersion, spec.GitHub?.Commitish, configPath);
                    ApplyDotNetPublishSkipFlags(dotNetPlan, request.SkipRestore, request.SkipBuild);
                    result.DotNetToolPlan = dotNetPlan;

                    if (!request.PlanOnly && !request.ValidateOnly)
                    {
                        request.CancellationToken.ThrowIfCancellationRequested();
                        var toolProgress = request.Progress is IPowerForgeReleaseProgressReporterV2 detailedProgress
                            ? new DotNetPublishReleaseProgressAdapter(detailedProgress, dotNetPlan)
                            : null;
                        var dotNetTools = _runDotNetTools(dotNetPlan, toolProgress, request.CancellationToken);
                        request.CancellationToken.ThrowIfCancellationRequested();
                        FilterDotNetToolResult(dotNetTools, selectedToolOutputs);
                        result.DotNetTools = dotNetTools;
                        if (!dotNetTools.Succeeded)
                        {
                            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Tools, dotNetTools.ErrorMessage);
                            result.Success = false;
                            result.ErrorMessage = dotNetTools.ErrorMessage ?? "DotNet tool release workflow failed.";
                            return result;
                        }

                        var publishToolGitHub = request.PublishToolGitHub ?? spec.Tools!.GitHub.Publish;
                        if (publishToolGitHub)
                        {
                            ValidatePostBuildSourceState(request);
                            var releases = PublishDotNetToolGitHubReleases(
                                spec,
                                configDirectory,
                                dotNetPlan,
                                dotNetTools,
                                sharedReleaseVersion,
                                request.CancellationToken);
                            result.ToolGitHubReleases = releases;
                            var failures = releases.Where(entry => !entry.Success).ToArray();
                            if (failures.Length > 0)
                            {
                                request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Tools, failures[0].ErrorMessage);
                                result.Success = false;
                                result.ErrorMessage = failures[0].ErrorMessage ?? "Tool GitHub release publishing failed.";
                                return result;
                            }
                        }
                    }
                }
            }
            else
            {
                if (willRunTools)
                {
                    var legacyToolTargets = GetSectionSelectedTargets(selectedTargets, toolTargetMatches, runAppleApps, appleTargetMatches);
                    var toolPlan = WithRequestTargets(
                        request,
                        legacyToolTargets,
                        () => _planTools(spec.Tools!, configPath, request));
                    result.ToolPlan = toolPlan;
                    if (!string.IsNullOrWhiteSpace(spec.Outputs?.PowerForgeToolManifestPath) &&
                        ResolveSelectedToolOutputs(request).Contains(PowerForgeReleaseToolOutputKind.Tool) &&
                        IsStandalonePowerForgeToolSelected(result))
                    {
                        VerifySharedReleaseSourceCommit(toolPlan.ProjectRoot, spec.GitHub?.Commitish, configPath);
                    }

                    if (!request.PlanOnly && !request.ValidateOnly)
                    {
                        request.CancellationToken.ThrowIfCancellationRequested();
                        var tools = _runTools(
                            toolPlan,
                            request.Progress as IPowerForgeReleaseProgressReporterV2,
                            request.CancellationToken);
                        request.CancellationToken.ThrowIfCancellationRequested();
                        result.Tools = tools;
                        if (!tools.Success)
                        {
                            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Tools, tools.ErrorMessage);
                            result.Success = false;
                            result.ErrorMessage = tools.ErrorMessage ?? "Tool release workflow failed.";
                            return result;
                        }

                        var publishToolGitHub = request.PublishToolGitHub ?? spec.Tools!.GitHub.Publish;
                        if (publishToolGitHub)
                        {
                            ValidatePostBuildSourceState(request);
                            var releases = PublishLegacyToolGitHubReleases(
                                spec,
                                configDirectory,
                                tools,
                                request.CancellationToken);
                            result.ToolGitHubReleases = releases;
                            var failures = releases.Where(entry => !entry.Success).ToArray();
                            if (failures.Length > 0)
                            {
                                request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Tools, failures[0].ErrorMessage);
                                result.Success = false;
                                result.ErrorMessage = failures[0].ErrorMessage ?? "Tool GitHub release publishing failed.";
                                return result;
                            }
                        }
                    }
                }
            }
            if (willRunTools)
            {
                var outputCount = result.ToolPlan?.Targets.Sum(target => target.Combinations.Length)
                    ?? result.DotNetToolPlan?.Steps.Length
                    ?? CountConfiguredToolOutputs(spec.Tools!, dotNetSpecForTools);
                var outputDetail = request.PlanOnly || request.ValidateOnly
                    ? $"{outputCount} planned output step(s)"
                    : $"{outputCount} output step(s) completed";
                request.Progress?.PhaseCompleted(PowerForgeReleaseProgressPhase.Tools, outputDetail);
            }
        }
        if (coordinateModuleAndPackageVersions && !request.PlanOnly && !request.ValidateOnly)
        {
            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.Packages,
                CountConfiguredPackageProjects(spec.Packages!),
                $"Building and publishing version {request.ResolvedReleaseVersion}");
            var packages = ExecutePackageRelease(
                spec.Packages!,
                configPath,
                request,
                configurationOverride,
                publishUnifiedGitHub,
                request.ResolvedReleaseVersion,
                spec.Module!.VersionPrimaryProject);
            result.Packages = packages;
            if (!packages.Success)
            {
                request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.Packages, packages.ErrorMessage);
                result.Success = false;
                result.ErrorMessage = packages.ErrorMessage ?? "Package release workflow failed.";
                return result;
            }

            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.Packages,
                $"Version {request.ResolvedReleaseVersion}");
        }
        if (applePlan is not null)
        {
            if (!request.PlanOnly &&
                !request.ValidateOnly &&
                (!request.CheckpointAppleApps || applePlan.Archive))
            {
                request.Progress?.PhaseStarted(
                    PowerForgeReleaseProgressPhase.AppleApps,
                    applePlan.Apps.Length,
                    $"{applePlan.Action}: {applePlan.Apps.Length} Apple target(s)");
                if (request.Progress is IPowerForgeReleaseProgressReporterV2 detailedAppleProgress)
                {
                    detailedAppleProgress.ItemsPlanned(
                        PowerForgeReleaseProgressPhase.AppleApps,
                        applePlan.Apps.Select((app, index) => new PowerForgeReleaseProgressItem
                        {
                            Phase = PowerForgeReleaseProgressPhase.AppleApps,
                            Key = "apple:" + app.Name,
                            Title = app.Name,
                            Kind = applePlan.Action.ToString(),
                            Target = app.Platform.ToString(),
                            CounterLabel = "Target",
                            Position = index + 1,
                            Total = applePlan.Apps.Length
                        }).ToArray());
                }
                using var operationLock = AppleReleaseOperationLock.Acquire(applePlan.LockPath, applePlan.Action);
                var cleanup = new PowerForgeAppleReleaseCleanupReceipt();
                PowerForgeAppleAppReleaseResult[] appleResults;
                PowerForgeAppleVersionReceipt? appleVersioning = null;
                var receiptJournalReady = !applePlan.Automation.WriteReceipt;
                try
                {
                    VerifyExpectedAppleCheckpointArchives(applePlan);
                    var approvedPlan = AssertApplePlanStillApproved(applePlan, request.AppleExpectedPlanSha256);
                    PrepareAppleReceiptJournalForMutation(applePlan, request.AppleExpectedPlanSha256);
                    receiptJournalReady = true;
                    if (applePlan.Action == PowerForgeAppleReleaseAction.Version)
                    {
                        appleVersioning = SelectAppleVersion(
                            applePlan,
                            approvedPlan?.Versioning ?? throw new InvalidOperationException(
                                "Apple Version execution requires one approved remote version observation."));
                        appleResults = RunAppleVersion(applePlan);
                    }
                    else if (request.CheckpointAppleApps)
                    {
                        appleResults = RunAppleArchiveCheckpoint(applePlan, out cleanup);
                    }
                    else if (applePlan.Action == PowerForgeAppleReleaseAction.Cleanup)
                    {
                        cleanup = _appleArtifactService.RemoveStaleArtifacts(
                            applePlan,
                            GetProtectedAppleRecoveryArtifactPaths(applePlan));
                        appleResults = applePlan.Apps
                            .Select(app => new PowerForgeAppleAppReleaseResult
                            {
                                Plan = app,
                                Success = true
                            })
                            .ToArray();
                    }
                    else
                    {
                        appleResults = RunAppleRelease(applePlan, out cleanup);
                    }
                }
                catch (Exception exception)
                {
                    appleResults = applePlan.Apps
                        .Select(app => new PowerForgeAppleAppReleaseResult
                        {
                            Plan = app,
                            Success = false,
                            ErrorMessage = exception.Message,
                            RemoteState = exception is AppleBuildProcessingException processing
                                ? processing.State
                                : null
                        })
                        .ToArray();
                }
                result.AppleApps = appleResults;
                if (request.CheckpointAppleApps && appleResults.All(static app => app.Success))
                    result.AppleReceipt = CreateApplePlanReceipt(applePlan, appleResults);
                if (receiptJournalReady &&
                    !request.CheckpointAppleApps &&
                    (applePlan.Action != PowerForgeAppleReleaseAction.Configured ||
                     HasAppleExecutionMutation(applePlan) ||
                     appleResults.Any(static app => !app.Success)))
                    result.AppleReceipt ??= CompleteAppleReleaseReceipt(applePlan, appleResults, cleanup, appleVersioning);

                if (result.AppleReceipt is { Success: false } failedReceipt)
                {
                    request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.AppleApps, failedReceipt.ErrorMessage);
                    result.Success = false;
                    result.ErrorMessage = failedReceipt.ErrorMessage ?? "Apple release diagnostics failed.";
                    return result;
                }

                var failure = appleResults.FirstOrDefault(entry => !entry.Success);
                if (failure is not null)
                {
                    request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.AppleApps, failure.ErrorMessage);
                    result.Success = false;
                    result.ErrorMessage = failure.ErrorMessage ?? $"Apple app release failed for '{failure.Plan.Name}'.";
                    return result;
                }
                request.Progress?.PhaseCompleted(
                    PowerForgeReleaseProgressPhase.AppleApps,
                    $"{applePlan.Action} completed for {applePlan.Apps.Length} target(s)");
            }
        }

        if (deferredModulePublishRequest is not null &&
            !request.PlanOnly &&
            !request.ValidateOnly)
        {
            ValidatePostBuildSourceState(request);
            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.Module,
                1,
                "Publishing the checkpointed PowerShell module after prerequisite lanes");
            var modulePublication = ExecuteModuleRequest(
                deferredModulePublishRequest,
                ConfigurationGateMode.Publish,
                includeModulePublishing: true,
                noDotnetBuild: true,
                skipInstall: true,
                includeProjectPackages: false,
                reuseStaging: deferredModulePublishRequest.ConfigPath is not null,
                cancellationToken: request.CancellationToken);
            result.ModulePublication = modulePublication;
            if (!modulePublication.Succeeded)
            {
                var moduleSource = deferredModulePublishRequest.ConfigPath
                    ?? deferredModulePublishRequest.ScriptPath
                    ?? "module publish";
                request.Progress?.PhaseFailed(
                    PowerForgeReleaseProgressPhase.Module,
                    BuildModuleFailureMessage(moduleSource, modulePublication));
                result.Success = false;
                result.ErrorMessage = BuildModuleFailureMessage(
                    moduleSource,
                    modulePublication);
                return result;
            }

            if (captureModuleArtifactProvenance)
            {
                result.ModuleProducedAssets = ResolveProducedModuleArtifacts(
                    result.ModuleAssets,
                    moduleArtifactBaseline);
            }

            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.Module,
                "Module publication complete");
        }

        if (!request.PlanOnly && !request.ValidateOnly && !explicitAppleAction)
        {
            ValidatePostBuildSourceState(request);
            PopulateReleaseOutputs(spec, request, configDirectory, result, sharedReleaseVersion);
            result.ToolGitHubReleasePlans = BuildToolGitHubReleasePlans(spec, result);
            GenerateWingetOutputs(spec, request, configDirectory, result);
            IncludeWingetOutputsInReleaseAssets(result);
            RewriteReleaseSummaryFiles(result);
            if (publishUnifiedGitHub)
            {
                ValidatePostBuildSourceState(request);
                request.Progress?.PhaseStarted(
                    PowerForgeReleaseProgressPhase.GitHub,
                    result.ReleaseAssets.Length,
                    $"Uploading {result.ReleaseAssets.Length} release asset(s)");
                var unifiedGitHubRelease = PublishUnifiedGitHubRelease(
                    spec,
                    configDirectory,
                    result,
                    sharedReleaseVersion,
                    request.Progress,
                    request.CancellationToken);
                result.UnifiedGitHubRelease = unifiedGitHubRelease;
                if (!unifiedGitHubRelease.Success)
                {
                    request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.GitHub, unifiedGitHubRelease.ErrorMessage);
                    result.Success = false;
                    result.ErrorMessage = unifiedGitHubRelease.ErrorMessage ?? "Unified GitHub release publishing failed.";
                    return result;
                }
                request.Progress?.PhaseCompleted(
                    PowerForgeReleaseProgressPhase.GitHub,
                    unifiedGitHubRelease.ReleaseUrl ?? "GitHub release published");
            }
            ValidatePostBuildSourceState(request);
            SubmitWingetOutputs(spec, request, configDirectory, result);
            if (result.Success && publishVirusTotalMonitor)
            {
                ValidatePostBuildSourceState(request);
                if (!TryPublishVirusTotalMonitor(spec, request, configDirectory, result, sharedReleaseVersion, virusTotalApiKey))
                    return result;
            }
        }

        return result;
    }

    private static void ApplyExplicitToolOnlyReleaseVersion(
        PowerForgeReleaseRequest request,
        bool runModule,
        bool runPackages,
        bool runTools,
        bool runAppleApps)
    {
        if (string.IsNullOrWhiteSpace(request.ReleaseVersion))
            return;

        if (!request.ToolsOnly || !runTools || runModule || runPackages || runAppleApps)
        {
            throw new InvalidOperationException(
                "ReleaseVersion is supported only for an explicitly tool-only release. Use ToolsOnly to keep module, package, and Apple lanes out of the publication.");
        }

        var version = request.ReleaseVersion!.Trim();
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("ReleaseVersion must use exact x.y.z format.", nameof(request));

        request.ResolvedReleaseVersion = version;
    }

    internal static PowerForgeReleaseSpec LoadConfiguration(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("ConfigPath is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Unified release config was not found: {fullPath}", fullPath);

        var bytes = File.ReadAllBytes(fullPath);
        string content;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = reader.ReadToEnd();

        var spec = LoadConfigurationContent(content, fullPath);
        spec.LoadedConfigurationPath = fullPath;
        spec.LoadedConfigurationSha256 = ComputeConfigurationSha256(bytes);
        return spec;
    }

    internal static PowerForgeReleaseSpec LoadConfigurationContent(string content, string sourcePath)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));
        PowerForgeReleaseConfigurationSecretValidator.ValidateJson(content);
        var spec = JsonSerializer.Deserialize<PowerForgeReleaseSpec>(content, CreateJsonOptions());
        return spec ?? throw new InvalidOperationException($"Unable to deserialize unified release config: {sourcePath}");
    }

    private static string ComputeConfigurationSha256(byte[] bytes)
    {
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal PowerForgeReleaseResult PublishBuiltReleaseOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult builtResult)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (builtResult is null)
            throw new ArgumentNullException(nameof(builtResult));
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required.", nameof(request));

        request.CancellationToken.ThrowIfCancellationRequested();

        var configPath = Path.GetFullPath(request.ConfigPath.Trim().Trim('"'));
        if (File.Exists(configPath))
            PowerForgeReleaseConfigurationSecretValidator.ValidateJson(File.ReadAllText(configPath));
        PowerForgeReleaseConfigurationSecretValidator.Validate(spec);
        if (!string.IsNullOrWhiteSpace(request.EffectiveConfigurationPath))
        {
            string effectiveConfigurationPath = Path.GetFullPath(
                request.EffectiveConfigurationPath!.Trim().Trim('"'));
            if (!AppleReleasePathsEqual(effectiveConfigurationPath, configPath))
            {
                PowerForgeReleaseConfigurationSecretValidator.ValidateJson(
                    File.ReadAllText(effectiveConfigurationPath));
            }
        }
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var publishVirusTotalMonitor = ShouldPublishVirusTotalMonitorFromCheckpoint(
            spec,
            builtResult,
            request.ModulePublisherActive);
        var virusTotalApiKey = PrepareVirusTotalPublishPreflight(
            spec,
            configPath,
            builtResult,
            request.ModulePublisherActive);
        var sharedReleaseVersion = request.ResolvedReleaseVersion ?? ResolveSharedReleaseVersion(spec, builtResult);

        if (spec.AppleApps is not null)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var appleResult = Execute(
                spec,
                new PowerForgeReleaseRequest {
                    ConfigPath = configPath,
                    AppleOnly = true,
                    AppleAction = request.AppleAction,
                    AppleMarketingVersion = request.AppleMarketingVersion,
                    AppleSourceCommit = request.AppleSourceCommit,
                    RequireImmutableAppleSourceSnapshot =
                        request.RequireImmutableAppleSourceSnapshot ||
                        !string.IsNullOrWhiteSpace(request.AppleSourceCommit),
                    AppleExpectedPlanSha256 = request.AppleExpectedPlanSha256,
                    AppleExpectedArchiveSha256ByTarget = new Dictionary<string, string>(
                        request.AppleExpectedArchiveSha256ByTarget,
                        StringComparer.OrdinalIgnoreCase),
                    AppleActionConfirmed = request.AppleActionConfirmed,
                    AppleAdoptExistingBuild = request.AppleAdoptExistingBuild,
                    AppleResume = request.AppleResume,
                    AppleWaitForProcessing = request.AppleWaitForProcessing,
                    AppleProcessingTimeoutSeconds = request.AppleProcessingTimeoutSeconds,
                    ApplePollIntervalSeconds = request.ApplePollIntervalSeconds,
                    Configuration = request.Configuration,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    PublishProjectGitHub = false,
                    PublishToolGitHub = false,
                    SubmitWinget = false,
                    ResolvedReleaseVersion = sharedReleaseVersion,
                    CancellationToken = request.CancellationToken
                });
            builtResult.AppleAppPlan = appleResult.AppleAppPlan;
            builtResult.AppleApps = appleResult.AppleApps;
            builtResult.AppleReceipt = appleResult.AppleReceipt;
            if (!appleResult.Success)
            {
                builtResult.Success = false;
                builtResult.ErrorMessage = appleResult.ErrorMessage ?? "Apple application release failed.";
                return builtResult;
            }
        }

        if (spec.Tools is not null && (request.PublishToolGitHub ?? spec.Tools.GitHub.Publish))
        {
            ValidatePostBuildSourceState(request);
            request.CancellationToken.ThrowIfCancellationRequested();
            if (builtResult.DotNetToolPlan is not null && builtResult.DotNetTools is not null)
            {
                builtResult.ToolGitHubReleases = PublishDotNetToolGitHubReleases(
                    spec,
                    configDirectory,
                    builtResult.DotNetToolPlan,
                    builtResult.DotNetTools,
                    sharedReleaseVersion,
                    request.CancellationToken);
            }
            else if (builtResult.Tools is not null)
            {
                builtResult.ToolGitHubReleases = PublishLegacyToolGitHubReleases(
                    spec,
                    configDirectory,
                    builtResult.Tools,
                    request.CancellationToken);
            }

            var toolFailure = builtResult.ToolGitHubReleases.FirstOrDefault(static release => !release.Success);
            if (toolFailure is not null)
            {
                builtResult.Success = false;
                builtResult.ErrorMessage = toolFailure.ErrorMessage ?? "Tool GitHub release publishing failed.";
                return builtResult;
            }
        }

        var moduleSelected = spec.Module is not null && !request.PackagesOnly && !request.ToolsOnly;
        if (ShouldPublishUnifiedGitHub(spec, request, moduleSelected))
        {
            ValidatePostBuildSourceState(request);
            request.CancellationToken.ThrowIfCancellationRequested();
            var unified = PublishUnifiedGitHubRelease(
                spec,
                configDirectory,
                builtResult,
                sharedReleaseVersion,
                request.Progress,
                request.CancellationToken);
            builtResult.UnifiedGitHubRelease = unified;
            if (!unified.Success)
            {
                builtResult.Success = false;
                builtResult.ErrorMessage = unified.ErrorMessage ?? "Unified GitHub release publishing failed.";
                return builtResult;
            }
        }

        ValidatePostBuildSourceState(request);
        request.CancellationToken.ThrowIfCancellationRequested();
        SubmitWingetOutputs(spec, request, configDirectory, builtResult);
        request.CancellationToken.ThrowIfCancellationRequested();
        if (!builtResult.Success)
            return builtResult;

        if (publishVirusTotalMonitor)
        {
            ValidatePostBuildSourceState(request);
            if (!TryPublishVirusTotalMonitor(
                    spec,
                    request,
                    configDirectory,
                    builtResult,
                    sharedReleaseVersion,
                    virusTotalApiKey))
            {
                return builtResult;
            }
        }

        builtResult.Success = true;
        builtResult.ErrorMessage = null;
        return builtResult;
    }

    private static TResult WithRequestTargets<TResult>(PowerForgeReleaseRequest request, string[] targets, Func<TResult> action)
    {
        var originalTargets = request.Targets;
        request.Targets = targets;
        try
        {
            return action();
        }
        finally
        {
            request.Targets = originalTargets;
        }
    }

    private static bool ShouldRunSectionForTargets(
        string[] selectedTargets,
        string[] sectionTargetMatches,
        bool hasOtherTargetSection,
        string[] otherSectionTargetMatches)
    {
        if (selectedTargets.Length == 0 || sectionTargetMatches.Length > 0)
            return true;

        return !hasOtherTargetSection || otherSectionTargetMatches.Length == 0;
    }

    private static string[] GetSectionSelectedTargets(
        string[] selectedTargets,
        string[] sectionTargetMatches,
        bool hasOtherTargetSection,
        string[] otherSectionTargetMatches)
    {
        if (selectedTargets.Length == 0)
            return Array.Empty<string>();

        if (sectionTargetMatches.Length > 0)
            return sectionTargetMatches;

        return hasOtherTargetSection && otherSectionTargetMatches.Length > 0
            ? Array.Empty<string>()
            : selectedTargets;
    }

    private static string[] ResolveLegacyToolTargetMatches(PowerForgeToolReleaseSpec tools, string[] selectedTargets)
    {
        if (selectedTargets.Length == 0)
            return Array.Empty<string>();

        return selectedTargets
            .Where(selected => (tools.Targets ?? Array.Empty<PowerForgeToolReleaseTarget>())
                .Any(target => string.Equals(target.Name?.Trim(), selected, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string[] ResolveDotNetToolTargetMatches(DotNetPublishSpec spec, string[] selectedTargets)
    {
        if (selectedTargets.Length == 0)
            return Array.Empty<string>();

        var activeTargets = ResolveDotNetProfileTargetNames(spec);
        return selectedTargets
            .Where(selected => activeTargets.Contains(selected))
            .ToArray();
    }

    private static string[] ResolveOptionalDotNetToolTargetMatches(DotNetPublishSpec? spec, string[] selectedTargets)
    {
        return spec is null ? Array.Empty<string>() : ResolveDotNetToolTargetMatches(spec, selectedTargets);
    }

    private static HashSet<string> ResolveDotNetProfileTargetNames(DotNetPublishSpec spec)
    {
        var targetNames = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            .Where(target => target is not null && !string.IsNullOrWhiteSpace(target.Name))
            .Select(target => target.Name!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var profiles = (spec.Profiles ?? Array.Empty<DotNetPublishProfile>())
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .ToArray();
        if (profiles.Length == 0)
            return targetNames;

        var profileName = !string.IsNullOrWhiteSpace(spec.Profile)
            ? spec.Profile!.Trim()
            : profiles.FirstOrDefault(profile => profile.Default)?.Name;
        if (string.IsNullOrWhiteSpace(profileName))
            return targetNames;

        var profile = profiles.FirstOrDefault(profile => profile.Name!.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        var profileTargets = profile?.Targets ?? Array.Empty<string>();
        if (profile is null || profileTargets.Length == 0)
            return targetNames;

        return profileTargets
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Where(targetNames.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ResolveAppleTargetMatches(PowerForgeAppleReleaseOptions options, string[] selectedTargets)
    {
        if (selectedTargets.Length == 0)
            return Array.Empty<string>();

        var apps = (options.Apps ?? Array.Empty<AppleAppConfiguration>())
            .Where(app => app.Enabled)
            .ToArray();

        return selectedTargets
            .Where(selected => apps.Any(app => AppleAppMatchesTarget(app, selected)))
            .ToArray();
    }

    private static bool AppleTargetSelectionUsesNameOrScheme(PowerForgeAppleReleaseOptions options, string[] selectedTargets)
    {
        if (selectedTargets.Length == 0)
            return false;

        var apps = (options.Apps ?? Array.Empty<AppleAppConfiguration>())
            .Where(app => app.Enabled)
            .ToArray();

        return selectedTargets.Any(selected =>
            apps.Any(app => AppleAppNameOrSchemeMatchesTarget(app, selected)));
    }

    private static void ValidateSelectedTargets(
        string[] selectedTargets,
        string[] toolTargetMatches,
        string[] appleTargetMatches,
        bool runTools,
        bool runAppleApps)
    {
        if ((!runTools && !runAppleApps) || selectedTargets.Length == 0)
            return;

        var knownTargets = toolTargetMatches
            .Concat(appleTargetMatches)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = selectedTargets
            .Where(selected => !knownTargets.Contains(selected))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown release target(s): {string.Join(", ", missing)}", nameof(PowerForgeReleaseRequest.Targets));
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
        string? configurationOverride,
        bool packagePublishingRequested,
        bool publishUnifiedGitHub)
    {
        var configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = Path.GetFullPath(Path.IsPathRooted(options.RepositoryRoot)
            ? options.RepositoryRoot!
            : Path.Combine(configDirectory, string.IsNullOrWhiteSpace(options.RepositoryRoot) ? "." : options.RepositoryRoot!));
        var hasConfigPath = !string.IsNullOrWhiteSpace(options.ConfigPath);
        var hasScriptPath = !string.IsNullOrWhiteSpace(options.ScriptPath);
        if (hasConfigPath && hasScriptPath)
            throw new InvalidOperationException("Module.ConfigPath and Module.ScriptPath are mutually exclusive.");

        var configPath = hasConfigPath
            ? Path.GetFullPath(Path.IsPathRooted(options.ConfigPath)
                ? options.ConfigPath!
                : Path.Combine(repositoryRoot, options.ConfigPath!))
            : null;
        var scriptPath = hasConfigPath
            ? null
            : Path.GetFullPath(Path.IsPathRooted(options.ScriptPath)
                ? options.ScriptPath!
                : Path.Combine(repositoryRoot, string.IsNullOrWhiteSpace(options.ScriptPath) ? Path.Combine("Module", "Build", "Build-Module.ps1") : options.ScriptPath!));
        var effectiveConfiguration = configurationOverride ?? "Release";
        // JSON-backed module builds need an explicit modern host selection. Legacy
        // script entry points historically left Framework unset so they could run
        // under Windows PowerShell or older compatible PowerShell Core hosts.
        var effectiveModuleFramework = request.ModuleFramework
            ?? options.Framework
            ?? (hasConfigPath ? "auto" : null);
        var moduleImportFramework = string.Equals(effectiveModuleFramework, "auto", StringComparison.OrdinalIgnoreCase)
            ? "net8.0"
            : effectiveModuleFramework ?? "net8.0";
        var requestedModulePath = request.ModuleHostPath ?? options.ModulePath;
        var configuredModulePath = ExpandModulePath(requestedModulePath, effectiveConfiguration, moduleImportFramework);
        var modulePath = string.IsNullOrWhiteSpace(requestedModulePath)
            ? "PSPublishModule"
            : Path.IsPathRooted(configuredModulePath)
                ? configuredModulePath
                : Path.Combine(repositoryRoot, configuredModulePath);
        var manifestPath = string.IsNullOrWhiteSpace(options.ManifestPath)
            ? null
            : Path.GetFullPath(Path.IsPathRooted(options.ManifestPath)
                ? options.ManifestPath!
                : Path.Combine(repositoryRoot, options.ManifestPath!));

        if (configPath is not null && !File.Exists(configPath))
            throw new FileNotFoundException($"Module build config was not found: {configPath}", configPath);
        var moduleConfig = configPath is null
            ? null
            : new ModulePipelineConfigurationService().Load(configPath);
        if (scriptPath is not null && !File.Exists(scriptPath))
            throw new FileNotFoundException($"Module build script was not found: {scriptPath}", scriptPath);
        if (!request.PlanOnly &&
            !request.ValidateOnly &&
            !string.IsNullOrWhiteSpace(requestedModulePath) &&
            !File.Exists(modulePath))
            throw new FileNotFoundException($"Configured module assembly was not found: {modulePath}", modulePath);
        if (!string.IsNullOrWhiteSpace(manifestPath) && !File.Exists(manifestPath))
            throw new FileNotFoundException($"Module manifest was not found: {manifestPath}", manifestPath);

        var artifactPaths = (options.ArtifactPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PathTokenProtection.GetFullPath(repositoryRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var timeoutSeconds = request.ModuleTimeoutSeconds ?? options.TimeoutSeconds;
        if (timeoutSeconds <= 0)
            throw new InvalidOperationException("Module TimeoutSeconds must be greater than zero.");
        var includeProjectPackages = options.IncludesPackages && !request.ModuleOnly;
        var noDotnetBuildOverride = request.ModuleNoDotnetBuild ?? options.NoDotnetBuild;
        var signModuleOverride = request.ModuleSignModule ?? options.SignModule;
        var moduleName = string.IsNullOrWhiteSpace(options.ModuleName)
            ? moduleConfig?.Spec.Build.Name
            : options.ModuleName;

        var buildRequest = new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = repositoryRoot,
            ConfigPath = configPath,
            ScriptPath = scriptPath,
            ModulePath = modulePath,
            Configuration = configurationOverride,
            Framework = effectiveModuleFramework,
            RunMode = ResolveModuleRunMode(options, request, packagePublishingRequested),
            PowerForgeReleaseStage = true,
            UnifiedGitHubRelease = publishUnifiedGitHub,
            NoDotnetBuild = noDotnetBuildOverride ?? false,
            NoDotnetBuildWasSpecified = noDotnetBuildOverride.HasValue,
            ModuleVersion = string.IsNullOrWhiteSpace(request.ResolvedReleaseVersion)
                ? request.ModuleVersion ?? options.ModuleVersion
                : PackageVersionUtility.GetNumericVersion(request.ResolvedReleaseVersion!),
            PreReleaseTag = string.IsNullOrWhiteSpace(request.ResolvedReleaseVersion)
                ? request.ModulePreReleaseTag ?? options.PreReleaseTag
                : PackageVersionUtility.GetPrereleaseVersion(request.ResolvedReleaseVersion!) ?? string.Empty,
            StagingPath = request.ModuleStagingPath,
            NoSign = request.ModuleNoSign ?? options.NoSign ?? false,
            SkipInstall = request.ModuleSkipInstall ?? false,
            SignModule = signModuleOverride ?? false,
            SignModuleWasSpecified = signModuleOverride.HasValue,
            IncludeProjectPackages = includeProjectPackages,
            IncludeModulePublishing = request.ModuleIncludePublishing ?? true,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            CertificateThumbprint = request.ModuleCertificateThumbprint,
            SignIncludeBinaries = request.ModuleSignIncludeBinaries,
            SignIncludeInternals = request.ModuleSignIncludeInternals,
            SignIncludeExe = request.ModuleSignIncludeExe,
            DiagnosticsBaselinePath = request.ModuleDiagnosticsBaselinePath,
            GenerateDiagnosticsBaseline = request.ModuleGenerateDiagnosticsBaseline,
            UpdateDiagnosticsBaseline = request.ModuleUpdateDiagnosticsBaseline,
            FailOnNewDiagnostics = request.ModuleFailOnNewDiagnostics,
            FailOnDiagnosticsSeverity = request.ModuleFailOnDiagnosticsSeverity
        };
        buildRequest.Progress = request.Progress as IPowerForgeReleaseProgressReporterV2;

        var plan = new PowerForgeModuleReleasePlanSummary
        {
            ModuleName = moduleName,
            RepositoryRoot = repositoryRoot,
            ConfigPath = configPath,
            ScriptPath = scriptPath,
            ModulePath = modulePath,
            ManifestPath = manifestPath,
            Configuration = buildRequest.Configuration,
            Framework = buildRequest.Framework,
            RunMode = buildRequest.RunMode ?? ConfigurationGateMode.Build,
            IncludesPackages = options.IncludesPackages,
            IncludesProjectPackages = includeProjectPackages,
            TimeoutSeconds = timeoutSeconds,
            NoDotnetBuild = buildRequest.NoDotnetBuild,
            ModuleVersion = buildRequest.ModuleVersion,
            PreReleaseTag = NullIfEmpty(buildRequest.PreReleaseTag ?? string.Empty),
            StagingPath = buildRequest.StagingPath,
            PackedModuleRoots = ResolvePackedModuleRoots(
                moduleConfig,
                moduleName,
                buildRequest.ModuleVersion,
                buildRequest.PreReleaseTag),
            NoSign = buildRequest.NoSign,
            SkipInstall = buildRequest.SkipInstall,
            SignModule = buildRequest.SignModule,
            PowerForgeReleaseStage = buildRequest.PowerForgeReleaseStage,
            UnifiedGitHubRelease = buildRequest.UnifiedGitHubRelease,
            ArtifactPaths = artifactPaths
        };

        return (buildRequest, plan, artifactPaths);
    }

    private static string ExpandModulePath(string? path, string configuration, string framework)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : ReplaceModulePathToken(
                ReplaceModulePathToken(path!, "{Configuration}", configuration),
                "{Framework}",
                framework);

    private static string ReplaceModulePathToken(string path, string token, string value)
        => System.Text.RegularExpressions.Regex.Replace(
            path,
            System.Text.RegularExpressions.Regex.Escape(token),
            _ => value,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static string[] ExpandModuleArtifactPaths(
        IEnumerable<string>? artifactPaths,
        string? moduleName,
        string? moduleVersion,
        string? preReleaseTag)
    {
        if (string.IsNullOrWhiteSpace(moduleName) ||
            string.IsNullOrWhiteSpace(NormalizeReleaseVersion(moduleVersion)))
        {
            return (artifactPaths ?? Array.Empty<string>()).ToArray();
        }

        return (artifactPaths ?? Array.Empty<string>())
            .Select(path => ModulePathTokenFormatter.ReplacePathTokens(
                path,
                moduleName!,
                moduleVersion!,
                preReleaseTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConfigurationGateMode ResolveModuleRunMode(
        PowerForgeModuleReleaseOptions options,
        PowerForgeReleaseRequest request,
        bool packagePublishingRequested)
    {
        if (request.ModuleRunMode.HasValue)
            return request.ModuleRunMode.Value;

        return options.IncludesPackages && packagePublishingRequested
            ? ConfigurationGateMode.Publish
            : ConfigurationGateMode.Build;
    }

    private PowerForgeAppleReleasePlan PrepareAppleRelease(
        PowerForgeAppleReleaseOptions options,
        PowerForgeReleaseRequest request,
        string releaseConfigPath,
        string? configurationOverride,
        string? sharedReleaseVersion,
        bool skipBuild,
        string[]? selectedTargetNames,
        bool allowUnresolvedResolvedVersion = false)
    {
        if (skipBuild && options.Archive)
            throw new InvalidOperationException("PowerForge release SkipBuild is not supported when AppleApps.Archive is enabled. Set AppleApps.Archive=false to reuse an existing Apple archive explicitly.");

        var configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var projectRoot = ResolveOutputPath(configDirectory, string.IsNullOrWhiteSpace(options.ProjectRoot) ? "." : options.ProjectRoot!);
        var configuration = configurationOverride ?? NormalizeAppleConfiguration(options.Configuration) ?? "Release";
        var archiveRoot = ResolveOutputPath(projectRoot, string.IsNullOrWhiteSpace(options.ArchiveRoot) ? Path.Combine("Artifacts", "Apple", "Archives") : options.ArchiveRoot!);
        var exportRoot = ResolveOutputPath(projectRoot, string.IsNullOrWhiteSpace(options.ExportRoot) ? Path.Combine("Artifacts", "Apple", "Exports") : options.ExportRoot!);
        var screenshotConfigPath = string.IsNullOrWhiteSpace(options.ScreenshotConfigPath)
            ? null
            : ResolveOutputPath(projectRoot, options.ScreenshotConfigPath!);
        var screenshotConfigPaths = (options.ScreenshotConfigPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(projectRoot, path))
            .ToArray();
        var metadataConfigPath = string.IsNullOrWhiteSpace(options.MetadataConfigPath)
            ? null
            : ResolveOutputPath(projectRoot, options.MetadataConfigPath!);
        var metadataConfigPaths = (options.MetadataConfigPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(projectRoot, path))
            .ToArray();
        var appInfoConfigPath = string.IsNullOrWhiteSpace(options.AppInfoConfigPath)
            ? null
            : ResolveOutputPath(projectRoot, options.AppInfoConfigPath!);
        var appInfoConfigPaths = (options.AppInfoConfigPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(projectRoot, path))
            .ToArray();
        var governanceConfigPath = string.IsNullOrWhiteSpace(options.GovernanceConfigPath)
            ? null
            : ResolveOutputPath(projectRoot, options.GovernanceConfigPath!);
        var governanceConfigPaths = (options.GovernanceConfigPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(projectRoot, path))
            .ToArray();
        foreach (var input in new[] { screenshotConfigPath, metadataConfigPath, appInfoConfigPath, governanceConfigPath }
                     .Where(static path => path is not null)
                     .Concat(screenshotConfigPaths)
                     .Concat(metadataConfigPaths)
                     .Concat(appInfoConfigPaths)
                     .Concat(governanceConfigPaths))
        {
            EnsureInputFileWithinProjectRoot(projectRoot, input!, "AppleApps input config");
        }
        var automation = options.Automation ?? new PowerForgeAppleReleaseAutomationOptions();
        var directDistribution = options.DirectDistribution ?? new PowerForgeAppleDirectDistributionOptions();
        automation.Resume = request.AppleResume ?? automation.Resume;
        automation.WaitForProcessing = request.AppleWaitForProcessing ?? automation.WaitForProcessing;
        automation.ProcessingTimeoutSeconds = request.AppleProcessingTimeoutSeconds ?? automation.ProcessingTimeoutSeconds;
        automation.PollIntervalSeconds = request.ApplePollIntervalSeconds ?? automation.PollIntervalSeconds;
        ValidateAppleAutomation(automation);
        var requestedMarketingVersion = FirstNonEmpty(
            request.AppleMarketingVersion,
            automation.MarketingVersionPattern);
        if (directDistribution.TimeoutSeconds <= 0)
            throw new InvalidOperationException("AppleApps.DirectDistribution.TimeoutSeconds must be greater than zero.");
        var receiptPath = ResolveOutputPath(projectRoot, automation.ReceiptPath);
        EnsurePathWithinProjectRoot(projectRoot, receiptPath, "AppleApps.Automation.ReceiptPath");
        var receiptHistoryPath = ResolveOutputPath(projectRoot, automation.ReceiptHistoryPath);
        EnsurePathWithinProjectRoot(projectRoot, receiptHistoryPath, "AppleApps.Automation.ReceiptHistoryPath");
        var planReceiptPath = ResolveOutputPath(projectRoot, automation.PlanReceiptPath);
        EnsurePathWithinProjectRoot(projectRoot, planReceiptPath, "AppleApps.Automation.PlanReceiptPath");
        var lockPath = ResolveOutputPath(projectRoot, automation.LockPath);
        EnsurePathWithinProjectRoot(projectRoot, lockPath, "AppleApps.Automation.LockPath");
        var versionSourcePath = string.IsNullOrWhiteSpace(automation.VersionSourcePath)
            ? null
            : ResolveOutputPath(projectRoot, automation.VersionSourcePath!);
        if (versionSourcePath is not null)
            EnsurePathWithinProjectRoot(projectRoot, versionSourcePath, "AppleApps.Automation.VersionSourcePath");

        var selectedTargets = NormalizeStrings(selectedTargetNames);
        var configuredApps = (options.Apps ?? Array.Empty<AppleAppConfiguration>())
            .Where(app => app.Enabled)
            .ToArray();
        if (request.AppleAction == PowerForgeAppleReleaseAction.Version && selectedTargets.Length > 0)
        {
            throw new InvalidOperationException(
                "Apple action 'Version' cannot be restricted with --target because the checked-in version source and build identity are shared across every configured Apple platform.");
        }
        if (selectedTargets.Length > 0)
        {
            var missing = selectedTargets
                .Where(selected => configuredApps.All(app => !AppleAppMatchesTarget(app, selected)))
                .ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown Apple app target(s): {string.Join(", ", missing)}", nameof(selectedTargetNames));
        }

        var apps = configuredApps
            .Where(app => selectedTargets.Length == 0 || selectedTargets.Any(selected => AppleAppMatchesTarget(app, selected)))
            .Select(app => PrepareAppleAppPlan(
                app,
                options,
                projectRoot,
                archiveRoot,
                exportRoot,
                configuration,
                sharedReleaseVersion,
                allowUnresolvedResolvedVersion,
                allowMissingProject: request.AppleAction == PowerForgeAppleReleaseAction.Cleanup))
            .ToArray();

        foreach (var app in apps)
        {
            if (!options.Archive &&
                options.Upload &&
                ShouldExecuteAppleTarget(request.AppleAction, app) &&
                (File.Exists(app.ArchivePath) || Directory.Exists(app.ArchivePath)))
            {
                app.ExpectedArchiveSha256 = AppleNotarizationService.ComputeArtifactSha256(app.ArchivePath);
            }
            if (request.AppleExpectedArchiveSha256ByTarget.TryGetValue(app.Name, out var expectedArchiveSha256))
            {
                var expected = expectedArchiveSha256.Trim();
                if (expected.Length != 64 || expected.Any(static value => !Uri.IsHexDigit(value)))
                    throw new InvalidOperationException($"The expected Apple archive SHA-256 for '{app.Name}' is invalid.");
                app.ExpectedArchiveSha256 = expected.ToLowerInvariant();
            }
        }
        var unknownExpectedArchiveTargets = request.AppleExpectedArchiveSha256ByTarget.Keys
            .Where(name => apps.All(app => !app.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (unknownExpectedArchiveTargets.Length > 0)
        {
            throw new InvalidOperationException(
                $"Expected Apple archive evidence references unknown target(s): {string.Join(", ", unknownExpectedArchiveTargets)}");
        }

        if (apps.Length == 0)
            throw new InvalidOperationException("AppleApps.Apps must contain at least one enabled app entry.");
        var duplicateName = apps
            .GroupBy(static app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"AppleApps target names must be unique. '{duplicateName.Key}' is configured more than once. " +
                "Give each platform target a stable name so receipts and artifact paths cannot collide.");
        }
        ValidateAppleProductTopology(apps);
        ValidateUniqueAppleArtifactPaths(
            apps,
            static app => app.ArchivePath,
            "archive");
        ValidateUniqueAppleArtifactPaths(
            apps,
            static app => app.ExportPath,
            "export");
        var protectedApplePaths = new List<(string Name, string Path, bool IsDirectory)>
        {
            ("release configuration", releaseConfigPath, false),
            ("archive root", archiveRoot, true),
            ("export root", exportRoot, true)
        };
        if (versionSourcePath is not null)
            protectedApplePaths.Add(("version source", versionSourcePath, false));
        protectedApplePaths.AddRange(
            new[] { screenshotConfigPath, metadataConfigPath, appInfoConfigPath, governanceConfigPath }
                .Where(static path => path is not null)
                .Select(static path => ("Apple mutation input", path!, false)));
        protectedApplePaths.AddRange(screenshotConfigPaths.Select(static path => ("screenshot config", path, false)));
        AddAppleScreenshotProtectedPaths(
            projectRoot,
            new[] { screenshotConfigPath }
                .Where(static path => path is not null)
                .Select(static path => path!)
                .Concat(screenshotConfigPaths),
            protectedApplePaths);
        protectedApplePaths.AddRange(metadataConfigPaths.Select(static path => ("metadata config", path, false)));
        protectedApplePaths.AddRange(appInfoConfigPaths.Select(static path => ("App Information config", path, false)));
        protectedApplePaths.AddRange(governanceConfigPaths.Select(static path => ("governance config", path, false)));
        foreach (var app in apps)
        {
            protectedApplePaths.Add(($"{app.Name} Xcode container", app.ProjectPath, Directory.Exists(app.ProjectPath)));
            protectedApplePaths.Add(($"{app.Name} archive", app.ArchivePath, true));
            protectedApplePaths.Add(($"{app.Name} export", app.ExportPath, true));
        }
        ValidateAppleAutomationOutputPaths(
            receiptPath,
            receiptHistoryPath,
            planReceiptPath,
            lockPath,
            protectedApplePaths);
        var appStoreConnectAction = request.AppleAction == PowerForgeAppleReleaseAction.Status ||
                                    request.AppleAction == PowerForgeAppleReleaseAction.Doctor ||
                                    request.AppleAction == PowerForgeAppleReleaseAction.Version ||
                                    IsUploadAction(request.AppleAction) ||
                                    options.PrepareDistribution ||
                                    options.SyncScreenshots ||
                                    options.SyncMetadata ||
                                    options.SyncAppInfo ||
                                    options.CheckGovernance ||
                                    options.CheckReleaseReadiness ||
                                    options.DistributeTestFlight ||
                                    options.SubmitTestFlightBetaReview ||
                                    options.SubmitForReview ||
                                    options.ReleaseApprovedVersion;
        var appStoreConnectApps = apps.Where(UsesAppStoreConnect).ToArray();
        var requiresAppStoreConnect = appStoreConnectAction && appStoreConnectApps.Length > 0;
        if (request.AppleAction == PowerForgeAppleReleaseAction.Version)
        {
            if (string.IsNullOrWhiteSpace(requestedMarketingVersion))
            {
                throw new InvalidOperationException(
                    "Apple action 'Version' requires --apple-version or AppleApps.Automation.MarketingVersionPattern.");
            }
            if (versionSourcePath is null)
                throw new InvalidOperationException("Apple action 'Version' requires AppleApps.Automation.VersionSourcePath.");
            if (!File.Exists(versionSourcePath))
                throw new FileNotFoundException($"Apple version source was not found: {versionSourcePath}", versionSourcePath);
        }
        if (options.DistributeTestFlight &&
            NormalizeStrings(options.TestFlightBetaGroupIds).Length == 0 &&
            NormalizeStrings(options.TestFlightBetaGroupNames).Length == 0)
            throw new InvalidOperationException("AppleApps DistributeTestFlight requires TestFlightBetaGroupIds or TestFlightBetaGroupNames.");
        if (options.SubmitTestFlightBetaReview && apps.All(static app => !UsesExternalTestFlight(app)))
        {
            throw new InvalidOperationException(
                "AppleApps SubmitTestFlightBetaReview requires at least one target with TestFlightPolicy=External. " +
                "Automatic and Internal policies cannot authorize the protected Beta App Review mutation.");
        }
        if (options.SyncScreenshots && screenshotConfigPath is null && screenshotConfigPaths.Length == 0)
            throw new InvalidOperationException("AppleApps SyncScreenshots requires ScreenshotConfigPath or ScreenshotConfigPaths.");
        var appleSourceCommit = request.AppleSourceCommit?.Trim() ?? string.Empty;
        if (appleSourceCommit.Length > 0 && !GitObjectId.IsFull(appleSourceCommit))
        {
            throw new InvalidOperationException("Apple source commit must be a full SHA-1 or SHA-256 Git commit object id.");
        }
        if (request.AppleAdoptExistingBuild &&
            !IsUploadAction(request.AppleAction) &&
            !(request.AppleAction == PowerForgeAppleReleaseAction.Configured && options.Upload))
        {
            throw new InvalidOperationException(
                "Adopting an existing Apple build is supported only for an upload execution.");
        }
        if (request.AppleAdoptExistingBuild && !automation.Resume)
        {
            throw new InvalidOperationException(
                "Adopting an existing Apple build requires AppleApps.Automation.Resume=true and cannot be combined with --no-apple-resume.");
        }
        if (options.SyncMetadata && metadataConfigPath is null && metadataConfigPaths.Length == 0)
            throw new InvalidOperationException("AppleApps SyncMetadata requires MetadataConfigPath or MetadataConfigPaths.");
        if (options.SyncAppInfo && appInfoConfigPath is null && appInfoConfigPaths.Length == 0)
            throw new InvalidOperationException("AppleApps SyncAppInfo requires AppInfoConfigPath or AppInfoConfigPaths.");
        if (options.CheckGovernance && governanceConfigPath is null && governanceConfigPaths.Length == 0)
            throw new InvalidOperationException("AppleApps CheckGovernance requires GovernanceConfigPath or GovernanceConfigPaths.");
        if (request.AppleAction == PowerForgeAppleReleaseAction.Configured &&
            !options.Archive &&
            apps.Any(app => app.VersionUpdateRequested))
        {
            throw new InvalidOperationException(
                "Apple app version updates require AppleApps.Archive=true for the configured legacy workflow. " +
                "Use an explicit Apple action such as Status or Prepare to select a configured release identity without mutating the project.");
        }
        var explicitAppStoreConnectApiConfiguredCount =
            (string.IsNullOrWhiteSpace(options.AppStoreConnectApiKeyPath) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(options.AppStoreConnectApiKeyId) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(options.AppStoreConnectApiIssuerId) ? 0 : 1);
        var allowAppStoreConnectEnvFallback = explicitAppStoreConnectApiConfiguredCount == 0;
        var configuredAppStoreConnectApiKeyPath = allowAppStoreConnectEnvFallback
            ? FirstNonEmpty(
                Environment.GetEnvironmentVariable("APP_STORE_CONNECT_PRIVATE_KEY_PATH"),
                Environment.GetEnvironmentVariable("ASC_PRIVATE_KEY_PATH"))
            : options.AppStoreConnectApiKeyPath;
        var appStoreConnectApiKeyPath = string.IsNullOrWhiteSpace(configuredAppStoreConnectApiKeyPath) ? null : ResolveOutputPath(projectRoot, configuredAppStoreConnectApiKeyPath!);
        var appStoreConnectApiKeyId = allowAppStoreConnectEnvFallback
            ? FirstNonEmpty(
                Environment.GetEnvironmentVariable("APP_STORE_CONNECT_KEY_ID"),
                Environment.GetEnvironmentVariable("ASC_KEY_ID"))
            : options.AppStoreConnectApiKeyId?.Trim();
        var appStoreConnectApiIssuerId = allowAppStoreConnectEnvFallback
            ? FirstNonEmpty(
                Environment.GetEnvironmentVariable("APP_STORE_CONNECT_ISSUER_ID"),
                Environment.GetEnvironmentVariable("ASC_ISSUER_ID"))
            : options.AppStoreConnectApiIssuerId?.Trim();
        var appStoreConnectApiConfiguredCount = (appStoreConnectApiKeyPath is null ? 0 : 1) + (appStoreConnectApiKeyId is null ? 0 : 1) + (appStoreConnectApiIssuerId is null ? 0 : 1);
        if (requiresAppStoreConnect && appStoreConnectApiConfiguredCount != 3)
            throw new InvalidOperationException("AppleApps PrepareDistribution, SyncMetadata, SyncAppInfo, SyncScreenshots, CheckReleaseReadiness, DistributeTestFlight, SubmitTestFlightBetaReview, SubmitForReview, or ReleaseApprovedVersion requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.");
        if (appStoreConnectApiConfiguredCount != 0)
        {
            if (appStoreConnectApiConfiguredCount != 3)
                throw new InvalidOperationException("AppleApps App Store Connect API-key authentication requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.");
            if ((options.Archive || options.Upload) &&
                appStoreConnectApps.Length > 0 &&
                !options.AllowProvisioningUpdates)
                throw new InvalidOperationException("AppleApps App Store Connect API-key authentication requires AllowProvisioningUpdates=true so xcodebuild can use the credentials.");
            if (!File.Exists(appStoreConnectApiKeyPath))
                throw new FileNotFoundException($"AppleApps App Store Connect API key file was not found: {appStoreConnectApiKeyPath}", appStoreConnectApiKeyPath);
        }

        if (RequiresDirectNotarizationCredentials(request.AppleAction, options.Upload, apps) &&
            string.IsNullOrWhiteSpace(directDistribution.KeychainProfile) &&
            appStoreConnectApiConfiguredCount != 3)
        {
            throw new InvalidOperationException(
                "Direct-notarized Apple targets require AppleApps.DirectDistribution.KeychainProfile or complete App Store Connect API-key credentials.");
        }

        if (requiresAppStoreConnect)
        {
            var credential = new AppStoreConnectApiCredential
            {
                IssuerId = appStoreConnectApiIssuerId!,
                KeyId = appStoreConnectApiKeyId!,
                PrivateKey = File.ReadAllText(appStoreConnectApiKeyPath!).Trim()
            };
            foreach (var app in appStoreConnectApps.Where(static app => string.IsNullOrWhiteSpace(app.AppStoreConnectAppId)))
                DiscoverAppStoreConnectAppId(app, credential, request.AppleAction);
        }

        var plan = new PowerForgeAppleReleasePlan
        {
            Progress = request.Progress as IPowerForgeReleaseProgressReporterV2,
            ProjectRoot = projectRoot,
            Configuration = configuration,
            Action = request.AppleAction,
            Automation = automation,
            DirectDistribution = directDistribution,
            ReceiptPath = receiptPath,
            ReceiptHistoryPath = receiptHistoryPath,
            PlanReceiptPath = planReceiptPath,
            LockPath = lockPath,
            VersionSourcePath = versionSourcePath,
            RequestedMarketingVersion = requestedMarketingVersion,
            SourceCommit = appleSourceCommit.Length == 0 ? null : appleSourceCommit,
            RequireImmutableSourceSnapshot = request.RequireImmutableAppleSourceSnapshot,
            ExactSourceConfigPath = request.RequireImmutableAppleSourceSnapshot && File.Exists(request.ConfigPath)
                ? request.ConfigPath
                : null,
            ExactSourceConfigSha256 = request.RequireImmutableAppleSourceSnapshot &&
                                      !string.IsNullOrWhiteSpace(request.ConfigPath) &&
                                      !string.IsNullOrWhiteSpace(request.LoadedConfigurationSha256)
                ? request.LoadedConfigurationSha256
                : null,
            AdoptExistingBuild = request.AppleAdoptExistingBuild,
            Archive = options.Archive,
            Upload = options.Upload,
            Rehearse = request.AppleAction == PowerForgeAppleReleaseAction.Rehearse,
            SyncScreenshots = options.SyncScreenshots,
            ScreenshotConfigPath = screenshotConfigPath,
            ScreenshotConfigPaths = screenshotConfigPaths,
            MetadataConfigPath = metadataConfigPath,
            MetadataConfigPaths = metadataConfigPaths,
            AppInfoConfigPath = appInfoConfigPath,
            AppInfoConfigPaths = appInfoConfigPaths,
            GovernanceConfigPath = governanceConfigPath,
            GovernanceConfigPaths = governanceConfigPaths,
            CheckGovernance = options.CheckGovernance,
            PrepareDistribution = options.PrepareDistribution,
            SelectBuildForDistribution = options.SelectBuildForDistribution,
            AllowUnprocessedDistributionBuild = options.AllowUnprocessedDistributionBuild,
            SyncMetadata = options.SyncMetadata,
            SyncAppInfo = options.SyncAppInfo,
            ReplaceScreenshots = options.ReplaceScreenshots,
            CheckReleaseReadiness = options.CheckReleaseReadiness,
            DistributeTestFlight = options.DistributeTestFlight,
            TestFlightBetaGroupIds = NormalizeStrings(options.TestFlightBetaGroupIds),
            TestFlightBetaGroupNames = NormalizeStrings(options.TestFlightBetaGroupNames),
            TestFlightTesterEmails = NormalizeStrings(options.TestFlightTesterEmails),
            CreateMissingTestFlightTesters = options.CreateMissingTestFlightTesters,
            AllowUnprocessedTestFlightBuild = options.AllowUnprocessedTestFlightBuild,
            SubmitTestFlightBetaReview = options.SubmitTestFlightBetaReview,
            SubmitForReview = options.SubmitForReview,
            AllowUnselectedReviewBuild = options.AllowUnselectedReviewBuild,
            AllowUnprocessedReviewBuild = options.AllowUnprocessedReviewBuild,
            SkipReviewReadinessCheck = options.SkipReviewReadinessCheck,
            AllowReviewSubmissionWhenNotReady = options.AllowReviewSubmissionWhenNotReady,
            ReleaseApprovedVersion = options.ReleaseApprovedVersion,
            AllowNonPendingDeveloperRelease = options.AllowNonPendingDeveloperRelease,
            XcodeBuildExecutable = string.IsNullOrWhiteSpace(options.XcodeBuildExecutable) ? "xcodebuild" : options.XcodeBuildExecutable.Trim(),
            AllowProvisioningUpdates = options.AllowProvisioningUpdates,
            ManageAppVersionAndBuildNumber = options.ManageAppVersionAndBuildNumber,
            UploadSymbols = options.UploadSymbols,
            GenerateAppStoreInformation = options.GenerateAppStoreInformation,
            SigningStyle = string.IsNullOrWhiteSpace(options.SigningStyle) ? "automatic" : options.SigningStyle!.Trim(),
            AppStoreConnectApiKeyPath = appStoreConnectApiKeyPath,
            AppStoreConnectApiKeyId = appStoreConnectApiKeyId,
            AppStoreConnectApiIssuerId = appStoreConnectApiIssuerId,
            Apps = apps
        };
        var validateReusableArchives =
            (!request.PlanOnly && !request.ValidateOnly) || request.CheckpointAppleApps;
        if (validateReusableArchives &&
            request.AppleAction == PowerForgeAppleReleaseAction.Configured &&
            !options.Archive &&
            options.Upload)
        {
            var missingArchive = apps.FirstOrDefault(app =>
                !Directory.Exists(app.ArchivePath) &&
                (!automation.Resume ||
                 !plan.AdoptExistingBuild ||
                 !HasPotentialVerifiedAppleUploadAttestation(plan, app)));
            if (missingArchive is not null)
            {
                throw new FileNotFoundException(
                    $"Apple app archive was not found for upload-only release and no exact upload attestation can resume it: {missingArchive.ArchivePath}",
                    missingArchive.ArchivePath);
            }
        }
        if (!request.PlanOnly &&
            !request.ValidateOnly &&
            !request.CheckpointAppleApps &&
            options.Upload &&
            automation.WaitForProcessing &&
            apps.Any(app => UsesAppStoreConnect(app) && string.IsNullOrWhiteSpace(app.AppStoreConnectAppId)))
        {
            throw new InvalidOperationException(
                "Apple upload processing waits require AppStoreConnectAppId for every App Store Connect target. " +
                "Configure the app id or explicitly disable WaitForProcessing for an upload-only handoff.");
        }
        return plan;
    }

    private static PowerForgeAppleAppReleaseTargetPlan PrepareAppleAppPlan(
        AppleAppConfiguration app,
        PowerForgeAppleReleaseOptions options,
        string projectRoot,
        string archiveRoot,
        string exportRoot,
        string configuration,
        string? sharedReleaseVersion,
        bool allowUnresolvedResolvedVersion,
        bool allowMissingProject)
    {
        if (string.IsNullOrWhiteSpace(app.ProjectPath))
            throw new InvalidOperationException("Apple app ProjectPath is required.");
        if (string.IsNullOrWhiteSpace(app.Scheme))
            throw new InvalidOperationException($"Apple app '{app.Name ?? app.ProjectPath}' requires Scheme for archive automation.");

        var name = string.IsNullOrWhiteSpace(app.Name) ? app.Scheme!.Trim() : app.Name!.Trim();
        var requestedProjectPath = ResolveOutputPath(projectRoot, app.ProjectPath);
        EnsurePathWithinProjectRoot(projectRoot, requestedProjectPath, $"Apple app '{name}' ProjectPath");
        var projectExists = File.Exists(requestedProjectPath) || Directory.Exists(requestedProjectPath);
        if (!projectExists && !app.GenerateProjectIfMissing && !allowMissingProject)
            throw new FileNotFoundException($"Apple app project or workspace was not found: {requestedProjectPath}", requestedProjectPath);
        if (app.ProjectGenerationTimeoutSeconds <= 0)
            throw new InvalidOperationException($"Apple app '{name}' ProjectGenerationTimeoutSeconds must be greater than zero.");
        var projectPath = projectExists
            ? NormalizeAppleArchiveProjectPath(requestedProjectPath, name)
            : ValidateGeneratedAppleProjectPath(requestedProjectPath, name);
        if (!allowMissingProject &&
            (!projectExists || app.RegenerateProject) &&
            !File.Exists(Path.Combine(Path.GetDirectoryName(projectPath) ?? projectRoot, "project.yml")))
        {
            throw new FileNotFoundException(
                $"Apple app '{name}' requires project.yml beside the generated Xcode project.",
                Path.Combine(Path.GetDirectoryName(projectPath) ?? projectRoot, "project.yml"));
        }
        var isWorkspace = projectPath.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase);
        var safeName = SanitizeStageEntryName(name).Replace(' ', '-');
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "AppleApp";
        var platform = app.Platform;
        var archiveVariant = app.ArchiveVariant;
        var destination = AppleAppArchiveService.GetGenericDestination(platform, archiveVariant);
        var archivePath = Path.Combine(archiveRoot, platform.ToString(), $"{safeName}.xcarchive");
        var exportPath = Path.Combine(exportRoot, platform.ToString(), safeName);
        var workspaceVersionMutationRequested = !allowMissingProject &&
                                                isWorkspace &&
                                                (app.UseResolvedVersion ||
                                                 app.BuildNumberPolicy != AppleBuildNumberPolicy.KeepExisting);
        if (workspaceVersionMutationRequested)
            throw new InvalidOperationException($"Apple app '{name}' uses a .xcworkspace ProjectPath. Use explicit MarketingVersion and BuildNumber as read-only release identities, or point version mutation at the owning .xcodeproj/project.pbxproj.");
        var versionUpdateRequested = !allowMissingProject &&
                                     !isWorkspace &&
                                     (app.UseResolvedVersion ||
                                      !string.IsNullOrWhiteSpace(app.MarketingVersion) ||
                                      !string.IsNullOrWhiteSpace(app.BuildNumber) ||
                                      app.BuildNumberPolicy != AppleBuildNumberPolicy.KeepExisting);
        var marketingVersion = isWorkspace
            ? app.MarketingVersion
            : versionUpdateRequested
                ? app.UseResolvedVersion ? sharedReleaseVersion : app.MarketingVersion
                : null;
        if (versionUpdateRequested && string.IsNullOrWhiteSpace(marketingVersion) && !(allowUnresolvedResolvedVersion && app.UseResolvedVersion))
            throw new InvalidOperationException($"Apple app '{name}' requires MarketingVersion unless UseResolvedVersion is enabled with a resolvable release version.");

        var buildNumberMustWaitForGeneration =
            app.BuildNumberPolicy == AppleBuildNumberPolicy.IncrementExisting &&
            (!projectExists || app.RegenerateProject);
        var buildNumber = isWorkspace
            ? string.IsNullOrWhiteSpace(app.BuildNumber) ? null : app.BuildNumber!.Trim()
            : versionUpdateRequested && !buildNumberMustWaitForGeneration
                ? ResolveAppleBuildNumber(app, projectPath, new XcodeProjectVersionEditor())
                : null;

        return new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = name,
            BundleId = app.BundleId,
            Platform = platform,
            ArchiveVariant = archiveVariant,
            DistributionRoute = app.DistributionRoute,
            ProductRole = app.ProductRole,
            ParentTarget = string.IsNullOrWhiteSpace(app.ParentTarget) ? null : app.ParentTarget!.Trim(),
            Capabilities = NormalizeStrings(app.Capabilities),
            TestFlightPolicy = app.TestFlightPolicy,
            RequiredEmbeddedBundleIds = NormalizeStrings(app.RequiredEmbeddedBundleIds),
            RequiredPrivacyUsageDescriptionKeys = NormalizeStrings(app.RequiredPrivacyUsageDescriptionKeys),
            AppStoreConnectAppId = string.IsNullOrWhiteSpace(app.AppStoreConnectAppId) ? null : app.AppStoreConnectAppId!.Trim(),
            ProjectPath = projectPath,
            IsWorkspace = isWorkspace,
            Scheme = app.Scheme!.Trim(),
            Configuration = configuration,
            Destination = destination,
            ArchivePath = archivePath,
            ExportPath = exportPath,
            TeamId = options.TeamId,
            Upload = options.Upload,
            VersionUpdateRequested = versionUpdateRequested,
            MarketingVersion = marketingVersion?.Trim(),
            BuildNumber = buildNumber,
            BuildNumberPolicy = app.BuildNumberPolicy,
            GenerateProjectIfMissing = app.GenerateProjectIfMissing,
            RegenerateProject = app.RegenerateProject,
            XcodeGenExecutable = string.IsNullOrWhiteSpace(app.XcodeGenExecutable) ? "xcodegen" : app.XcodeGenExecutable.Trim(),
            ProjectGenerationTimeoutSeconds = app.ProjectGenerationTimeoutSeconds
        };
    }

    private static void ValidateUniqueAppleArtifactPaths(
        PowerForgeAppleAppReleaseTargetPlan[] apps,
        Func<PowerForgeAppleAppReleaseTargetPlan, string> selector,
        string artifactKind)
    {
        var collision = apps
            .GroupBy(
                app => Path.GetFullPath(selector(app))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (collision is null)
            return;

        throw new InvalidOperationException(
            $"AppleApps target names produce the same {artifactKind} artifact path: {collision.Key}. " +
            $"Choose names that remain unique after file-name normalization ({string.Join(", ", collision.Select(static app => app.Name))}).");
    }

    private PowerForgeAppleAppReleaseResult[] RunAppleRelease(
        PowerForgeAppleReleasePlan plan,
        out PowerForgeAppleReleaseCleanupReceipt cleanup)
    {
        using var sourceSnapshot = AppleReleaseSourceSnapshot.CreateIfRequired(plan);
        cleanup = new PowerForgeAppleReleaseCleanupReceipt();
        var preflightCompleted = false;
        var releaseApps = plan.Apps
            .Where(app => ShouldExecuteAppleTarget(plan.Action, app))
            .ToArray();
        if (releaseApps.Length == 0 && IsNamedAppleMutationAction(plan.Action))
        {
            throw new InvalidOperationException(
                $"Apple action '{plan.Action}' cannot execute for any selected target. " +
                "Choose a target whose distribution route and TestFlight policy support the requested transition.");
        }
        var needsScreenshotSpecs = plan.SyncScreenshots ||
                                   plan.CheckReleaseReadiness ||
                                   (plan.SubmitForReview && !plan.SkipReviewReadinessCheck);
        var screenshotSpecs = needsScreenshotSpecs
            ? LoadAppleScreenshotSpecs(plan)
            : Array.Empty<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();
        var metadataSpecs = plan.SyncMetadata
            ? LoadAppleMetadataSpecs(plan)
            : Array.Empty<(AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)>();
        var appInfoSpecs = plan.SyncAppInfo
            ? LoadAppleAppInfoSpecs(plan)
            : Array.Empty<(AppStoreConnectAppInfoMetadataSpec Spec, string ConfigPath)>();
        var appInfoSpecsByAppId = plan.SyncAppInfo
            ? IndexAppInfoSpecsByAppId(
                appInfoSpecs,
                plan.Apps.Where(static app => app.DistributionRoute == AppleDistributionRoute.AppStore).ToArray())
            : new Dictionary<string, AppStoreConnectAppInfoMetadataSpec[]>(StringComparer.OrdinalIgnoreCase);
        var pendingAppInfoAppIds = new HashSet<string>(appInfoSpecsByAppId.Keys, StringComparer.OrdinalIgnoreCase);
        var governanceSpecsByAppId = plan.CheckGovernance
            ? LoadAppleGovernanceSpecs(plan)
            : new Dictionary<string, AppStoreConnectGovernanceSpec>(StringComparer.OrdinalIgnoreCase);
        var resultsByApp = plan.Apps.ToDictionary(
            static app => app,
            static app => new PowerForgeAppleAppReleaseResult
            {
                Plan = app,
                Success = true
            });
        foreach (var app in plan.Apps.Except(releaseApps))
        {
            resultsByApp[app].SkippedSteps = new[] { "independentRelease" };
        }
        var valuesByApp = new Dictionary<PowerForgeAppleAppReleaseTargetPlan, (string MarketingVersion, string BuildNumber)>();
        var screenshotsByApp = new Dictionary<
            PowerForgeAppleAppReleaseTargetPlan,
            (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)?>();
        var metadataByApp = new Dictionary<
            PowerForgeAppleAppReleaseTargetPlan,
            (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)?>();
        var resumedByApp = new Dictionary<PowerForgeAppleAppReleaseTargetPlan, bool>();
        var governancePlansByAppId = new Dictionary<string, AppStoreConnectGovernancePlan>(StringComparer.OrdinalIgnoreCase);
        var preflightAttempted = new HashSet<PowerForgeAppleAppReleaseTargetPlan>();

        foreach (var app in releaseApps)
        {
            var result = resultsByApp[app];
            preflightAttempted.Add(app);
            try
            {
                if (plan.Action != PowerForgeAppleReleaseAction.Cleanup)
                    result.ProjectGenerated = _generateAppleProject(app);
                if (plan.Action != PowerForgeAppleReleaseAction.Cleanup &&
                    app.VersionUpdateRequested &&
                    app.BuildNumberPolicy == AppleBuildNumberPolicy.IncrementExisting &&
                    string.IsNullOrWhiteSpace(app.BuildNumber))
                {
                    app.BuildNumber = ResolveAppleBuildNumber(
                        new AppleAppConfiguration
                        {
                            BuildNumberPolicy = app.BuildNumberPolicy
                        },
                        app.ProjectPath,
                        new XcodeProjectVersionEditor());
                }

                var needsReleaseIdentity = RequiresAppleReleaseIdentity(plan);
                if (needsReleaseIdentity)
                {
                    var values = ResolveAppleDistributionValues(app, versionUpdate: null);
                    app.MarketingVersion = values.MarketingVersion;
                    app.BuildNumber = values.BuildNumber;
                    valuesByApp[app] = values;
                }

                var matchingScreenshotSpec = app.DistributionRoute == AppleDistributionRoute.AppStore &&
                                             (plan.SyncScreenshots ||
                                              plan.CheckReleaseReadiness ||
                                              (plan.SubmitForReview && !plan.SkipReviewReadinessCheck))
                    ? ResolveMatchingScreenshotSpec(
                        screenshotSpecs,
                        app,
                        valuesByApp[app].MarketingVersion,
                        required: plan.SyncScreenshots || screenshotSpecs.Length > 0)
                    : null;
                if (matchingScreenshotSpec is not null)
                {
                    matchingScreenshotSpec = (
                        BindScreenshotSpec(
                            matchingScreenshotSpec.Value.Spec,
                            app,
                            valuesByApp[app].MarketingVersion),
                        matchingScreenshotSpec.Value.ConfigPath);
                }
                screenshotsByApp[app] = matchingScreenshotSpec;
                if (plan.SyncScreenshots && matchingScreenshotSpec is not null)
                    ValidateAppleScreenshotPreflight(matchingScreenshotSpec.Value, plan.SourceCommit);

                var matchingMetadataSpec = plan.SyncMetadata &&
                                           app.DistributionRoute == AppleDistributionRoute.AppStore
                    ? ResolveMatchingMetadataSpec(
                        metadataSpecs,
                        app,
                        valuesByApp[app].MarketingVersion,
                        required: true)
                    : null;
                metadataByApp[app] = matchingMetadataSpec;
                if (matchingMetadataSpec is not null)
                    ValidateAppleMetadataPreflight(matchingMetadataSpec.Value);

                if (plan.CheckGovernance && UsesAppStoreConnect(app))
                {
                    if (!governanceSpecsByAppId.TryGetValue(app.AppStoreConnectAppId!, out var governanceSpec))
                        throw new InvalidOperationException($"No governance config matches Apple app '{app.Name}' (AppStoreConnectAppId '{app.AppStoreConnectAppId}').");
                    if (!governancePlansByAppId.TryGetValue(app.AppStoreConnectAppId!, out var governancePlan))
                    {
                        governancePlan = _planAppleGovernance(CreateAppStoreConnectCredential(plan), governanceSpec);
                        governancePlansByAppId.Add(app.AppStoreConnectAppId!, governancePlan);
                    }
                    result.Governance = governancePlan;
                    if (!governancePlan.IsConverged)
                    {
                        throw new InvalidOperationException(
                            $"Apple governance drift blocks '{app.Name}': {governancePlan.DriftCount} change(s), " +
                            $"{governancePlan.BlockedCount} blocked, {governancePlan.Findings.Count(finding => finding.IsError)} config error(s). " +
                            "Review the governance plan receipt and run 'powerforge apple-governance apply --reviewed-plan <plan-receipt> --confirm'.");
                    }
                }

                var resumedUpload = UsesAppStoreConnect(app)
                    ? TryResumeAppleUpload(plan, app, result)
                    : app.DistributionRoute == AppleDistributionRoute.DirectNotarized &&
                      TryResumeDirectAppleNotarization(plan, app, result);
                resumedByApp[app] = resumedUpload;
                if (plan.Upload &&
                    !plan.Archive &&
                    !resumedUpload &&
                    !Directory.Exists(app.ArchivePath))
                {
                    throw new FileNotFoundException(
                        $"Apple app archive was not found for upload-only release: {app.ArchivePath}",
                        app.ArchivePath);
                }
            }
            catch (Exception exception)
            {
                foreach (var target in plan.Apps)
                {
                    var targetResult = resultsByApp[target];
                    targetResult.Success = false;
                    targetResult.SkippedSteps = MergeAppleSkippedSteps(
                        targetResult.SkippedSteps,
                        ReferenceEquals(target, app) || preflightAttempted.Contains(target)
                            ? new[] { "remoteActions" }
                            : new[] { "preflight", "remoteActions" });
                    if (ReferenceEquals(target, app))
                    {
                        targetResult.ErrorMessage = exception.Message;
                        if (exception is AppleBuildProcessingException processing)
                            targetResult.RemoteState = processing.State;
                    }
                }

                return plan.Apps.Select(target => resultsByApp[target]).ToArray();
            }
        }

        foreach (var app in releaseApps)
        {
            var result = resultsByApp[app];
            try
            {
                var resumedUpload = resumedByApp[app];
                using var archiveBuildSnapshot = plan.Archive && !resumedUpload
                    ? AppleArchiveBuildSnapshot.Create(app.ArchivePath)
                    : null;
                var approvedArchiveInputPath = app.ArchivePath;

                if (plan.Archive && !resumedUpload)
                {
                    if (!preflightCompleted)
                    {
                        cleanup = _appleArtifactService.Preflight(
                            plan,
                            GetProtectedAppleRecoveryArtifactPaths(plan));
                        preflightCompleted = true;
                    }
                }

                if (plan.Archive && app.VersionUpdateRequested && !resumedUpload)
                {
                    if (sourceSnapshot is not null)
                    {
                        throw new InvalidOperationException(
                            $"Apple app '{app.Name}' cannot mutate project versions while building an exact-source archive. " +
                            "Commit the requested version first, then create the checkpoint from that commit.");
                    }
                    result.VersionUpdate = new XcodeProjectVersionEditor().Update(app.ProjectPath, app.MarketingVersion!, app.BuildNumber);
                }

                if (plan.Archive && !resumedUpload)
                {
                    ReportAppleProgress(plan, app, PowerForgeReleaseProgressItemState.Started, "Preparing exact-source archive");
                    var directArchive = app.DistributionRoute == AppleDistributionRoute.DirectNotarized;
                    using var sourceMutationMonitor = sourceSnapshot?.MonitorChanges();
                    var archive = _archiveAppleApp(new AppleAppArchiveRequest
                    {
                        ProjectPath = sourceSnapshot?.MapPath(app.ProjectPath) ?? app.ProjectPath,
                        IsWorkspace = app.IsWorkspace,
                        Scheme = app.Scheme,
                        Configuration = app.Configuration,
                        Platform = app.Platform,
                        ArchiveVariant = app.ArchiveVariant,
                        Destination = app.Destination,
                        ArchivePath = archiveBuildSnapshot?.ArchivePath ?? app.ArchivePath,
                        XcodeBuildExecutable = plan.XcodeBuildExecutable,
                        RequireExactPackageSnapshot = sourceSnapshot is not null,
                        AllowProvisioningUpdates = plan.AllowProvisioningUpdates,
                        Progress = detail => ReportAppleProgress(
                            plan,
                            app,
                            PowerForgeReleaseProgressItemState.Started,
                            detail),
                        AppStoreConnectApiKeyPath = directArchive ? null : plan.AppStoreConnectApiKeyPath,
                        AppStoreConnectApiKeyId = directArchive ? null : plan.AppStoreConnectApiKeyId,
                        AppStoreConnectApiIssuerId = directArchive ? null : plan.AppStoreConnectApiIssuerId
                    });
                    result.Archive = archive;
                    sourceMutationMonitor?.ValidateNoChanges();
                    sourceSnapshot?.ValidateUnchanged();
                    if (!archive.Succeeded)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"xcodebuild archive failed for '{app.Name}' with exit code {archive.ProcessResult.ExitCode}.";
                        return CompleteAppleExecutionFailure(plan, resultsByApp, app);
                    }

                    var publishedArchiveSha256 = archiveBuildSnapshot!.Publish(app.ArchivePath, archive.ArchiveSha256);
                    approvedArchiveInputPath = archiveBuildSnapshot.ArchivePath;
                    result.ArchiveSha256 = publishedArchiveSha256;
                    app.ExpectedArchiveSha256 = publishedArchiveSha256;
                    archive.ArchivePath = app.ArchivePath;
                }

            if ((plan.Upload || plan.Rehearse) && result.Success && !resumedUpload)
            {
                ReportAppleProgress(
                    plan,
                    app,
                    PowerForgeReleaseProgressItemState.Started,
                    plan.Rehearse ? "Archive complete; validating local export" : "Archive complete; starting distribution export");
                CaptureAppleArchiveSha256(result, app);
                var approvedArchiveSha256 = result.ArchiveSha256;
                var direct = app.DistributionRoute == AppleDistributionRoute.DirectNotarized;
                var localExportOnly = plan.Rehearse || direct;
                using var uploadSnapshot = string.IsNullOrWhiteSpace(approvedArchiveSha256)
                    ? null
                    : AppleArchiveUploadSnapshot.Create(approvedArchiveInputPath, approvedArchiveSha256!);
                using var directExportSnapshot = localExportOnly ? AppleDirectExportSnapshot.Create() : null;
                using var uploadMutationMonitor = uploadSnapshot is null
                    ? null
                    : uploadSnapshot.MonitorChanges();
                AppleAppArchiveUploadResult upload;
                var uploadRemoteMutationStarted = false;
                try
                {
                    upload = _uploadAppleApp(new AppleAppArchiveUploadRequest
                    {
                        ArchivePath = uploadSnapshot?.ArchivePath ?? app.ArchivePath,
                        BundleId = app.BundleId,
                        RequiredPrivacyUsageDescriptionKeys = app.RequiredPrivacyUsageDescriptionKeys,
                        ExportPath = directExportSnapshot?.ExportPath ?? app.ExportPath,
                        TeamId = app.TeamId,
                        XcodeBuildExecutable = plan.XcodeBuildExecutable,
                        RequireTrustedSystemTools = !string.IsNullOrWhiteSpace(plan.SourceCommit),
                        SigningStyle = plan.SigningStyle,
                        Destination = localExportOnly ? "export" : "upload",
                        Method = direct ? plan.DirectDistribution.ExportMethod : "app-store-connect",
                        ManageAppVersionAndBuildNumber = plan.ManageAppVersionAndBuildNumber,
                        UploadSymbols = plan.UploadSymbols,
                        GenerateAppStoreInformation = !direct && plan.GenerateAppStoreInformation,
                        AppStoreConnectApiKeyPath = direct ? null : plan.AppStoreConnectApiKeyPath,
                        AppStoreConnectApiKeyId = direct ? null : plan.AppStoreConnectApiKeyId,
                        AppStoreConnectApiIssuerId = direct ? null : plan.AppStoreConnectApiIssuerId,
                        AllowProvisioningUpdates = plan.AllowProvisioningUpdates,
                        RemoteMutationStarted = plan.Rehearse ? null : () => uploadRemoteMutationStarted = true
                    });
                }
                catch (Exception ex) when (!direct && uploadRemoteMutationStarted)
                {
                    try
                    {
                        WriteAppleUploadAmbiguity(plan, app, result, $"The upload process did not return a definitive result: {ex.Message}");
                    }
                    catch (Exception checkpointException)
                    {
                        throw new AggregateException(
                            $"The App Store upload attempt for '{app.Name}' was indeterminate and its ambiguity checkpoint could not be persisted. Do not upload again until remote state is reconciled.",
                            ex,
                            checkpointException);
                    }
                    throw new InvalidOperationException(
                        $"The App Store upload attempt for '{app.Name}' ended without a definitive result. Do not upload again until App Store Connect state is reconciled.",
                        ex);
                }
                upload.ArchivePath = app.ArchivePath;
                result.Upload = upload;
                if (!direct && !plan.Rehearse && upload.Succeeded)
                {
                    try
                    {
                        WriteAppleUploadAttestation(plan, app, result);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"The App Store upload completed for '{app.Name}', but its durable upload attestation could not be persisted. " +
                            "Do not upload the archive again until App Store Connect state has been reconciled.",
                            ex);
                    }
                }
                else if (!direct && !plan.Rehearse && uploadRemoteMutationStarted)
                {
                    try
                    {
                        WriteAppleUploadAmbiguity(
                            plan,
                            app,
                            result,
                            $"xcodebuild exited with code {upload.ProcessResult.ExitCode} (timed out: {upload.ProcessResult.TimedOut}).");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"The App Store upload attempt for '{app.Name}' was indeterminate and its ambiguity checkpoint could not be persisted. " +
                            "Do not upload again until remote state is reconciled.",
                            ex);
                    }
                }
                if (localExportOnly && upload.Succeeded)
                    directExportSnapshot!.BindProducedArtifact(upload.ExportArtifactPath, upload.ExportArtifactSha256);
                uploadMutationMonitor?.ValidateNoChanges();
                if (uploadSnapshot is not null)
                    uploadSnapshot.ValidateUnchanged(approvedArchiveSha256!);
                if (!upload.Succeeded)
                {
                    result.Success = false;
                    result.ErrorMessage = $"xcodebuild exportArchive {(direct ? "Developer ID export" : "upload")} failed for '{app.Name}' with exit code {upload.ProcessResult.ExitCode}.";
                    return CompleteAppleExecutionFailure(plan, resultsByApp, app);
                }
                VerifyAppleArchiveUnchangedAfterUpload(app, result);

                if (localExportOnly)
                {
                    var published = directExportSnapshot!.Publish(app.ExportPath);
                    upload.ExportPath = published.ExportPath;
                    upload.ExportArtifactPath = published.ArtifactPath;
                    upload.ExportArtifactSha256 = published.ArtifactSha256;
                    upload.RehearsalArtifactSha256 = published.RehearsalArtifactSha256;
                    upload.RehearsalArtifactSha256Kind = published.RehearsalArtifactSha256Kind;
                    upload.ExportOptionsPlistPath = MapDirectExportOutputPath(
                        directExportSnapshot.ExportPath,
                        published.ExportPath,
                        upload.ExportOptionsPlistPath) ?? upload.ExportOptionsPlistPath;
                    upload.DistributionLogPath = MapDirectExportOutputPath(
                        directExportSnapshot.ExportPath,
                        published.ExportPath,
                        upload.DistributionLogPath);
                    if (!plan.Rehearse)
                    {
                        result.Notarization = NotarizeDirectAppleExport(
                            plan,
                            app,
                            published.ArtifactPath,
                            expectedArtifactSha256: published.ArtifactSha256);
                        WriteAppleNotarizationAttestation(plan, app, result);
                        if (!result.Notarization.Succeeded)
                            throw CreateAppleNotarizationFailure(app, result.Notarization);
                    }
                    directExportSnapshot.CommitPublication();
                }
                else
                {
                    if (IsUploadExecution(plan) &&
                        plan.Automation.WaitForProcessing &&
                        UsesAppStoreConnect(app))
                        result.RemoteState = WaitForAppleBuild(plan, app, buildUploadId: upload.BuildUploadId);
                }
            }

            var appInfoMetadataSpecs = app.DistributionRoute == AppleDistributionRoute.AppStore &&
                                       plan.SyncAppInfo &&
                                       pendingAppInfoAppIds.Remove(app.AppStoreConnectAppId!)
                ? appInfoSpecsByAppId[app.AppStoreConnectAppId!]
                : Array.Empty<AppStoreConnectAppInfoMetadataSpec>();
            if (app.DistributionRoute == AppleDistributionRoute.AppStore &&
                (plan.PrepareDistribution || plan.SyncScreenshots || plan.SyncMetadata || appInfoMetadataSpecs.Length > 0 || plan.CheckReleaseReadiness) &&
                result.Success)
            {
                var needsVersionDistribution = plan.PrepareDistribution ||
                                               plan.SyncScreenshots ||
                                               plan.SyncMetadata ||
                                               plan.CheckReleaseReadiness;
                var distributionValues = needsVersionDistribution
                    ? valuesByApp[app]
                    : (MarketingVersion: string.Empty, BuildNumber: string.Empty);
                var matchingScreenshotSpec = screenshotsByApp[app];
                var matchingMetadataSpec = metadataByApp[app];
                result.Distribution = _prepareAppleDistribution(new AppStoreConnectReleasePreparationRequest
                {
                    Credential = CreateAppStoreConnectCredential(plan),
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = distributionValues.MarketingVersion,
                    BuildNumber = distributionValues.BuildNumber,
                    Platform = app.Platform,
                    CreateVersion = plan.PrepareDistribution,
                    SelectBuild = plan.PrepareDistribution && plan.SelectBuildForDistribution,
                    RequireValidBuild = !plan.AllowUnprocessedDistributionBuild,
                    ScreenshotSpec = plan.SyncScreenshots ? matchingScreenshotSpec?.Spec : null,
                    MetadataSpec = matchingMetadataSpec?.Spec,
                    AppInfoMetadataSpecs = appInfoMetadataSpecs,
                    ReplaceScreenshots = plan.ReplaceScreenshots,
                    ExpectedSourceCommit = plan.SourceCommit,
                    ExpectedScreenshotFileSha256 = plan.ApprovedMutationInputFilesSha256.Count == 0
                        ? null
                        : plan.ApprovedMutationInputFilesSha256.ToDictionary(
                            value => Path.GetFullPath(Path.Combine(plan.ProjectRoot, value.Key)),
                            static value => value.Value,
                            Path.DirectorySeparatorChar == '\\'
                                ? StringComparer.OrdinalIgnoreCase
                                : StringComparer.Ordinal),
                    ExpectedScreenshotInventorySha256 = app.ExpectedScreenshotInventorySha256,
                    CheckReadiness = plan.CheckReleaseReadiness,
                    ReadinessRequest = plan.CheckReleaseReadiness && matchingScreenshotSpec is not null
                        ? new AppStoreConnectReleaseReadinessRequest
                        {
                            ScreenshotSpec = matchingScreenshotSpec.Value.Spec
                        }
                        : null,
                    BaseDirectory = matchingScreenshotSpec is null
                        ? matchingMetadataSpec is null
                            ? plan.ProjectRoot
                            : Path.GetDirectoryName(matchingMetadataSpec.Value.ConfigPath) ?? plan.ProjectRoot
                        : Path.GetDirectoryName(matchingScreenshotSpec.Value.ConfigPath) ?? plan.ProjectRoot
                });
                if (appInfoMetadataSpecs.Length > 0)
                    ValidateAppleAppInfoMutationResults(appInfoMetadataSpecs, result.Distribution.AppInfoMetadataResults);
            }

            if (plan.DistributeTestFlight && result.Success && UsesTestFlight(app))
            {
                var testFlightValues = valuesByApp[app];
                result.TestFlight = _distributeTestFlight(new AppStoreConnectTestFlightDistributionRequest
                {
                    Credential = CreateAppStoreConnectCredential(plan),
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = testFlightValues.MarketingVersion,
                    BuildNumber = testFlightValues.BuildNumber,
                    Platform = app.Platform,
                    BetaGroupIds = plan.TestFlightBetaGroupIds,
                    BetaGroupNames = plan.TestFlightBetaGroupNames,
                    TestFlightPolicy = app.TestFlightPolicy,
                    Testers = plan.TestFlightTesterEmails
                        .Select(static email => new AppStoreConnectBetaTesterSpec { Email = email })
                        .ToArray(),
                    CreateMissingTesters = plan.CreateMissingTestFlightTesters,
                    RequireValidBuild = !plan.AllowUnprocessedTestFlightBuild
                });
            }

            if (plan.SubmitTestFlightBetaReview && result.Success && UsesExternalTestFlight(app))
            {
                var testFlightValues = valuesByApp[app];
                result.TestFlightBetaReviewSubmission = _submitTestFlightBetaReview(new AppStoreConnectBetaAppReviewSubmissionRequest
                {
                    Credential = CreateAppStoreConnectCredential(plan),
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = testFlightValues.MarketingVersion,
                    BuildNumber = testFlightValues.BuildNumber,
                    Platform = app.Platform,
                    RequireValidBuild = !plan.AllowUnprocessedTestFlightBuild
                });
            }

            if (plan.SubmitForReview &&
                result.Success &&
                app.DistributionRoute == AppleDistributionRoute.AppStore)
            {
                var reviewValues = valuesByApp[app];
                var matchingScreenshotSpec = !plan.SkipReviewReadinessCheck
                    ? screenshotsByApp[app]
                    : null;
                result.ReviewSubmission = _submitAppleReview(new AppStoreConnectReviewSubmissionRequest
                {
                    Credential = CreateAppStoreConnectCredential(plan),
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = reviewValues.MarketingVersion,
                    BuildNumber = reviewValues.BuildNumber,
                    Platform = app.Platform,
                    RequireSelectedBuild = !plan.AllowUnselectedReviewBuild,
                    RequireValidBuild = !plan.AllowUnprocessedReviewBuild,
                    CheckReadiness = !plan.SkipReviewReadinessCheck,
                    RequireReady = !plan.AllowReviewSubmissionWhenNotReady,
                    ReadinessRequest = matchingScreenshotSpec is null
                        ? null
                        : new AppStoreConnectReleaseReadinessRequest
                        {
                            ScreenshotSpec = matchingScreenshotSpec.Value.Spec
                        }
                });
            }

            if (plan.ReleaseApprovedVersion &&
                result.Success &&
                app.DistributionRoute == AppleDistributionRoute.AppStore)
            {
                var releaseValues = valuesByApp[app];
                result.VersionRelease = _releaseAppleVersion(new AppStoreConnectVersionReleaseRequest
                {
                    Credential = CreateAppStoreConnectCredential(plan),
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = releaseValues.MarketingVersion,
                    Platform = app.Platform,
                    RequirePendingDeveloperRelease = !plan.AllowNonPendingDeveloperRelease
                });
            }

            ReportAppleProgress(plan, app, PowerForgeReleaseProgressItemState.Completed, "Apple target completed");
            }
            catch (Exception exception)
            {
                ReportAppleProgress(plan, app, PowerForgeReleaseProgressItemState.Failed, exception.Message);
                result.Success = false;
                result.ErrorMessage = exception.Message;
                if (exception is AppleBuildProcessingException processing)
                    result.RemoteState = processing.State;
                return CompleteAppleExecutionFailure(plan, resultsByApp, app);
            }
        }

        return plan.Apps.Select(app => resultsByApp[app]).ToArray();
    }

    private static void CaptureAppleArchiveSha256(
        PowerForgeAppleAppReleaseResult result,
        PowerForgeAppleAppReleaseTargetPlan app,
        string? requiredSha256 = null)
    {
        if (!File.Exists(app.ArchivePath) && !Directory.Exists(app.ArchivePath))
            return;
        var actual = AppleNotarizationService.ComputeArtifactSha256(app.ArchivePath);
        var expected = requiredSha256 ?? app.ExpectedArchiveSha256 ?? result.ArchiveSha256;
        if (!string.IsNullOrWhiteSpace(expected) &&
            !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The Apple archive for '{app.Name}' changed during upload or between approval and validation. " +
                $"Expected SHA-256 '{expected}', received '{actual}'. Rebuild and approve a new exact archive.");
        }
        result.ArchiveSha256 = actual;
        app.ExpectedArchiveSha256 = actual;
    }

    private PowerForgeAppleAppReleaseResult[] RunAppleArchiveCheckpoint(
        PowerForgeAppleReleasePlan plan,
        out PowerForgeAppleReleaseCleanupReceipt cleanup)
    {
        var checkpointPlan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = plan.ProjectRoot,
            Configuration = plan.Configuration,
            Action = PowerForgeAppleReleaseAction.Archive,
            Automation = new PowerForgeAppleReleaseAutomationOptions(),
            ReceiptPath = plan.ReceiptPath,
            ReceiptHistoryPath = plan.ReceiptHistoryPath,
            SourceCommit = plan.SourceCommit,
            RequireImmutableSourceSnapshot = plan.RequireImmutableSourceSnapshot,
            ExactSourceConfigPath = plan.ExactSourceConfigPath,
            ExactSourceConfigSha256 = plan.ExactSourceConfigSha256,
            Archive = true,
            Upload = false,
            XcodeBuildExecutable = plan.XcodeBuildExecutable,
            AllowProvisioningUpdates = plan.AllowProvisioningUpdates,
            ManageAppVersionAndBuildNumber = plan.ManageAppVersionAndBuildNumber,
            UploadSymbols = plan.UploadSymbols,
            GenerateAppStoreInformation = plan.GenerateAppStoreInformation,
            SigningStyle = plan.SigningStyle,
            AppStoreConnectApiKeyPath = plan.AppStoreConnectApiKeyPath,
            AppStoreConnectApiKeyId = plan.AppStoreConnectApiKeyId,
            AppStoreConnectApiIssuerId = plan.AppStoreConnectApiIssuerId,
            Apps = plan.Apps
        };
        var results = RunAppleRelease(checkpointPlan, out cleanup);
        if (results.All(static app => app.Success))
        {
            var missingEvidence = results.FirstOrDefault(result =>
                ShouldExecuteAppleTarget(PowerForgeAppleReleaseAction.Archive, result.Plan) &&
                string.IsNullOrWhiteSpace(result.ArchiveSha256));
            if (missingEvidence is not null)
            {
                throw new InvalidOperationException(
                    $"Apple archive checkpoint for '{missingEvidence.Plan.Name}' did not produce exact archive SHA-256 evidence.");
            }
            plan.Archive = false;
        }
        return results;
    }

    private static PowerForgeAppleAppReleaseResult[] CompleteAppleExecutionFailure(
        PowerForgeAppleReleasePlan plan,
        Dictionary<PowerForgeAppleAppReleaseTargetPlan, PowerForgeAppleAppReleaseResult> resultsByApp,
        PowerForgeAppleAppReleaseTargetPlan failedApp)
    {
        var failedIndex = Array.IndexOf(plan.Apps, failedApp);
        for (var index = failedIndex + 1; index < plan.Apps.Length; index++)
        {
            var untouched = resultsByApp[plan.Apps[index]];
            untouched.Success = false;
            untouched.SkippedSteps = MergeAppleSkippedSteps(
                untouched.SkippedSteps,
                new[] { "notAttempted" });
        }

        return plan.Apps.Select(app => resultsByApp[app]).ToArray();
    }

    private static string[] MergeAppleSkippedSteps(
        string[] existing,
        string[] additional)
        => existing
            .Concat(additional)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static AppStoreConnectReleasePreparationResult PrepareAppleDistribution(AppStoreConnectReleasePreparationRequest request)
    {
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectReleasePreparationService(client).PrepareAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectTestFlightDistributionResult DistributeTestFlight(AppStoreConnectTestFlightDistributionRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectBetaAppReviewSubmissionResult SubmitTestFlightBetaReview(AppStoreConnectBetaAppReviewSubmissionRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectBetaAppReviewSubmissionService(client).SubmitAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectReviewSubmissionResult SubmitAppleReview(AppStoreConnectReviewSubmissionRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectReviewSubmissionService(client).SubmitAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectReleaseReadinessResult CheckAppleReleaseReadiness(
        AppStoreConnectApiCredential credential,
        AppStoreConnectReleaseReadinessRequest request)
    {
        if (credential is null)
            throw new ArgumentNullException(nameof(credential));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        using var client = new AppStoreConnectClient(credential);
        return new AppStoreConnectReleaseReadinessService(client).CheckAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectVersionReleaseResult ReleaseAppleVersion(AppStoreConnectVersionReleaseRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectVersionReleaseService(client).ReleaseAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectGovernancePlan PlanAppleGovernance(
        AppStoreConnectApiCredential credential,
        AppStoreConnectGovernanceSpec spec)
    {
        using var client = new AppStoreConnectClient(credential);
        return new AppStoreConnectGovernanceService(client).PlanAsync(spec).GetAwaiter().GetResult();
    }

    private static AppStoreConnectApiCredential CreateAppStoreConnectCredential(PowerForgeAppleReleasePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.AppStoreConnectApiKeyPath) ||
            string.IsNullOrWhiteSpace(plan.AppStoreConnectApiKeyId) ||
            string.IsNullOrWhiteSpace(plan.AppStoreConnectApiIssuerId))
            throw new InvalidOperationException("AppleApps App Store Connect API-key authentication requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.");

        return new AppStoreConnectApiCredential
        {
            IssuerId = plan.AppStoreConnectApiIssuerId!.Trim(),
            KeyId = plan.AppStoreConnectApiKeyId!.Trim(),
            PrivateKey = File.ReadAllText(plan.AppStoreConnectApiKeyPath!).Trim()
        };
    }

    private static void ScrubApplePlanCredentials(PowerForgeAppleReleasePlan? plan)
    {
        if (plan is null)
            return;

        plan.AppStoreConnectApiKeyPath = null;
        plan.AppStoreConnectApiKeyId = null;
        plan.AppStoreConnectApiIssuerId = null;
    }

    private static (string MarketingVersion, string BuildNumber) ResolveAppleDistributionValues(
        PowerForgeAppleAppReleaseTargetPlan app,
        XcodeProjectVersionUpdateResult? versionUpdate)
    {
        var marketingVersion = versionUpdate?.After.MarketingVersion ?? app.MarketingVersion;
        var buildNumber = versionUpdate?.After.BuildNumber ?? app.BuildNumber;
        if (string.IsNullOrWhiteSpace(marketingVersion) || string.IsNullOrWhiteSpace(buildNumber))
        {
            if (app.IsWorkspace)
                throw new InvalidOperationException($"Apple app '{app.Name}' requires MarketingVersion and BuildNumber for Distribution preparation because workspace paths cannot be inspected for Xcode project versions.");

            var local = new XcodeProjectVersionEditor().Read(app.ProjectPath);
            if (string.IsNullOrWhiteSpace(marketingVersion))
                marketingVersion = local.MarketingVersion;
            if (string.IsNullOrWhiteSpace(buildNumber))
                buildNumber = local.BuildNumber;
        }

        if (string.IsNullOrWhiteSpace(marketingVersion))
            throw new InvalidOperationException($"Apple app '{app.Name}' requires a resolved marketing version before Distribution preparation.");
        if (string.IsNullOrWhiteSpace(buildNumber))
            throw new InvalidOperationException($"Apple app '{app.Name}' requires a resolved build number before Distribution preparation.");

        return (marketingVersion!.Trim(), buildNumber!.Trim());
    }

    private static (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] LoadAppleScreenshotSpecs(PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.ScreenshotConfigPath))
            paths.Add(plan.ScreenshotConfigPath!);
        paths.AddRange(plan.ScreenshotConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var json = ReadApprovedMutationInputText(plan, path);
                var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(json, CreateJsonOptions())
                    ?? throw new InvalidOperationException($"Unable to deserialize screenshot sync config: {path}");
                return (spec, path);
            })
            .ToArray();
    }

    private static (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] LoadAppleMetadataSpecs(PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.MetadataConfigPath))
            paths.Add(plan.MetadataConfigPath!);
        paths.AddRange(plan.MetadataConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var json = ReadApprovedMutationInputText(plan, path);
                var spec = JsonSerializer.Deserialize<AppStoreConnectVersionMetadataSpec>(json, CreateJsonOptions())
                    ?? throw new InvalidOperationException($"Unable to deserialize App Store version metadata config: {path}");
                return (spec, path);
            })
            .ToArray();
    }

    private static Dictionary<string, AppStoreConnectGovernanceSpec> LoadAppleGovernanceSpecs(PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.GovernanceConfigPath)) paths.Add(plan.GovernanceConfigPath!);
        paths.AddRange(plan.GovernanceConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));
        var configuration = new AppStoreConnectGovernanceConfiguration();
        var result = new Dictionary<string, AppStoreConnectGovernanceSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var spec = configuration.LoadContent(ReadApprovedMutationInputText(plan, path), path);
            var findings = configuration.Validate(spec);
            var errors = findings.Where(static finding => finding.IsError).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException($"Apple governance config '{path}' is invalid: {string.Join(" ", errors.Select(error => $"{error.Path}: {error.Message}"))}");
            if (result.ContainsKey(spec.AppId.Trim()))
                throw new InvalidOperationException($"Multiple Apple governance configs target AppStoreConnectAppId '{spec.AppId}'.");
            result.Add(spec.AppId.Trim(), spec);
        }
        return result;
    }

    private static void ValidateAppleScreenshotPreflight(
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath) configured,
        string? expectedSourceCommit)
    {
        var baseDirectory = Path.GetDirectoryName(configured.ConfigPath) ?? Directory.GetCurrentDirectory();
        var validation = new AppStoreConnectScreenshotSyncConfigValidator()
            .Validate(configured.Spec, baseDirectory, expectedSourceCommit: expectedSourceCommit);
        if (validation.IsValid)
            return;

        var messages = validation.Messages
            .Concat(validation.ScreenshotSets.SelectMany(static set => set.Messages))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        throw new InvalidOperationException(
            $"Screenshot preflight failed for '{configured.ConfigPath}': {string.Join(" ", messages)}");
    }

    private static void ValidateAppleMetadataPreflight(
        (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath) configured)
    {
        if (string.IsNullOrWhiteSpace(configured.Spec.Locale))
        {
            throw new InvalidOperationException(
                $"App Store version metadata config must declare Locale: {configured.ConfigPath}");
        }
        if (configured.Spec.Metadata is null)
        {
            throw new InvalidOperationException(
                $"App Store version metadata config must declare a Metadata object: {configured.ConfigPath}");
        }
    }

    private static (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)? ResolveMatchingScreenshotSpec(
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion,
        bool required = false)
    {
        var matches = specs
            .Where(candidate =>
                ScreenshotSpecMatches(candidate.Spec, app, marketingVersion))
            .ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"Multiple screenshot sync configs match Apple app '{app.Name}' version '{marketingVersion}' platform '{app.Platform}'.");
        if (matches.Length == 0 && required)
        {
            throw new InvalidOperationException(
                $"No screenshot sync config matches Apple app '{app.Name}' " +
                $"(AppStoreConnectAppId '{app.AppStoreConnectAppId}', platform '{app.Platform}', version '{marketingVersion}').");
        }

        return matches.Length == 0 ? null : matches[0];
    }

    private static bool ScreenshotSpecMatches(
        AppStoreConnectScreenshotSyncSpec spec,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion)
    {
        var appIdMatches = string.IsNullOrWhiteSpace(spec.AppId) ||
                           string.Equals(spec.AppId.Trim(), app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase);
        var specVersionString = string.IsNullOrWhiteSpace(spec.VersionString) ? null : spec.VersionString!.Trim();
        var versionMatches = (spec.UseReleaseVersion && specVersionString is null) ||
                             string.Equals(specVersionString, marketingVersion, StringComparison.OrdinalIgnoreCase);
        return appIdMatches && versionMatches && spec.Platform == app.Platform;
    }

    private static AppStoreConnectScreenshotSyncSpec BindScreenshotSpec(
        AppStoreConnectScreenshotSyncSpec source,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion)
        => new()
        {
            AppId = app.AppStoreConnectAppId!,
            VersionString = marketingVersion,
            VersionId = null,
            UseReleaseVersion = false,
            Platform = app.Platform,
            Locale = source.Locale,
            ScreenshotSets = source.ScreenshotSets,
            Quality = source.Quality
        };

    private static (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)? ResolveMatchingMetadataSpec(
        (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion,
        bool required = false)
    {
        var matches = specs
            .Where(candidate => MetadataSpecMatches(candidate.Spec, app, marketingVersion))
            .ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"Multiple App Store metadata configs match Apple app '{app.Name}' version '{marketingVersion}' platform '{app.Platform}'.");
        if (matches.Length == 0 && required)
        {
            throw new InvalidOperationException(
                $"No App Store metadata config matches Apple app '{app.Name}' " +
                $"(AppStoreConnectAppId '{app.AppStoreConnectAppId}', platform '{app.Platform}', version '{marketingVersion}').");
        }

        return matches.Length == 0 ? null : matches[0];
    }

    private static bool MetadataSpecMatches(
        AppStoreConnectVersionMetadataSpec spec,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion)
    {
        var appIdMatches = string.IsNullOrWhiteSpace(spec.AppId) ||
                           string.Equals(spec.AppId.Trim(), app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase);
        var specVersionString = string.IsNullOrWhiteSpace(spec.VersionString) ? null : spec.VersionString!.Trim();
        var versionMatches = (spec.UseReleaseVersion && specVersionString is null) ||
                             string.Equals(specVersionString, marketingVersion, StringComparison.OrdinalIgnoreCase);
        return appIdMatches && versionMatches && spec.Platform == app.Platform;
    }

    private static string NormalizeAppleArchiveProjectPath(string projectPath, string appName)
    {
        var normalizedProjectPath = TrimTrailingDirectorySeparators(projectPath);
        if (Directory.Exists(normalizedProjectPath))
        {
            var extension = Path.GetExtension(normalizedProjectPath);
            if (string.Equals(extension, ".xcodeproj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xcworkspace", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedProjectPath;
            }

            throw new InvalidOperationException($"Apple app '{appName}' ProjectPath must point to a .xcodeproj or .xcworkspace directory for archive automation: {normalizedProjectPath}");
        }

        if (File.Exists(normalizedProjectPath) &&
            string.Equals(Path.GetExtension(normalizedProjectPath), ".xcodeproj", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedProjectPath;
        }

        if (File.Exists(normalizedProjectPath) &&
            string.Equals(Path.GetFileName(normalizedProjectPath), "project.pbxproj", StringComparison.OrdinalIgnoreCase))
        {
            var projectDirectory = Path.GetDirectoryName(normalizedProjectPath);
            if (!string.IsNullOrWhiteSpace(projectDirectory) &&
                string.Equals(Path.GetExtension(projectDirectory), ".xcodeproj", StringComparison.OrdinalIgnoreCase))
            {
                return projectDirectory;
            }
        }

        throw new InvalidOperationException($"Apple app '{appName}' ProjectPath must point to a .xcodeproj or .xcworkspace for archive automation. project.pbxproj paths are only supported when they are inside a .xcodeproj directory.");
    }

    private static string ValidateGeneratedAppleProjectPath(string projectPath, string appName)
    {
        var normalized = TrimTrailingDirectorySeparators(projectPath);
        var extension = Path.GetExtension(normalized);
        if (!string.Equals(extension, ".xcodeproj", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xcworkspace", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Apple app '{appName}' generated ProjectPath must end with .xcodeproj or .xcworkspace: {normalized}");
        }
        return normalized;
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var trimmed = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(trimmed) ?? string.Empty;
        while (trimmed.Length > root.Length &&
               (trimmed[trimmed.Length - 1] == Path.DirectorySeparatorChar || trimmed[trimmed.Length - 1] == Path.AltDirectorySeparatorChar))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return trimmed;
    }

    private static bool AppleAppMatchesTarget(AppleAppConfiguration app, string targetName)
    {
        var trimmed = targetName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        return string.Equals(app.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(app.Scheme?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(app.BundleId?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AppleAppNameOrSchemeMatchesTarget(AppleAppConfiguration app, string targetName)
    {
        var trimmed = targetName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        return string.Equals(app.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(app.Scheme?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ResolveAppleBuildNumber(
        AppleAppConfiguration app,
        string projectPath,
        XcodeProjectVersionEditor editor)
    {
        if (!string.IsNullOrWhiteSpace(app.BuildNumber))
            return app.BuildNumber!.Trim();

        return app.BuildNumberPolicy switch
        {
            AppleBuildNumberPolicy.KeepExisting => null,
            AppleBuildNumberPolicy.Explicit => throw new InvalidOperationException("AppleApps.Apps.BuildNumber is required when BuildNumberPolicy is Explicit."),
            AppleBuildNumberPolicy.IncrementExisting => IncrementExistingAppleBuildNumber(editor.Read(projectPath)),
            _ => throw new InvalidOperationException($"Unsupported Apple build number policy: {app.BuildNumberPolicy}.")
        };
    }

    private static string IncrementExistingAppleBuildNumber(XcodeProjectVersionInfo info)
    {
        if (info.BuildNumber is null)
            throw new InvalidOperationException($"Cannot increment Apple build number because CURRENT_PROJECT_VERSION is missing or inconsistent in '{info.ProjectFilePath}'.");

        if (!long.TryParse(info.BuildNumber, out var current))
            throw new InvalidOperationException($"Cannot increment Apple build number '{info.BuildNumber}' in '{info.ProjectFilePath}'. Only integer build numbers are supported.");

        return (current + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    private static string? ResolveAppleSharedReleaseVersion(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult result)
    {
        var sharedReleaseVersion = ResolveSharedReleaseVersion(spec, result);
        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return sharedReleaseVersion;

        var moduleVersion = result.ModulePlan?.ModuleVersion
            ?? request.ModuleVersion
            ?? spec.Module?.ModuleVersion;
        return string.IsNullOrWhiteSpace(moduleVersion) ? null : moduleVersion;
    }

    private static Dictionary<string, string> BuildSharedReleaseVersionProperties(
        string sharedReleaseVersion,
        string? sourceCommit)
    {
        var trimmedSourceCommit = sourceCommit?.Trim();
        var exactSourceCommit = !string.IsNullOrWhiteSpace(trimmedSourceCommit) &&
                                Regex.IsMatch(trimmedSourceCommit!, "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)
            ? trimmedSourceCommit!.ToLowerInvariant()
            : null;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Version"] = sharedReleaseVersion,
            ["PackageVersion"] = sharedReleaseVersion,
            ["InformationalVersion"] = exactSourceCommit is null
                ? sharedReleaseVersion
                : sharedReleaseVersion + "+" + exactSourceCommit
        };
        if (exactSourceCommit is not null)
            properties["IncludeSourceRevisionInInformationalVersion"] = "false";

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
        var producedAssetEntries = CollectReleaseAssetEntries(result, result.DotNetToolPlan, sharedReleaseVersion)
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var additionalAssetPaths = ResolveAdditionalReleaseAssetPaths(
            spec,
            configDirectory,
            result,
            sharedReleaseVersion,
            producedAssetEntries,
            ResolveSelectedToolOutputs(request).Contains(PowerForgeReleaseToolOutputKind.Tool));
        var assetEntries = CollectReleaseAssetEntries(result, result.DotNetToolPlan, sharedReleaseVersion, additionalAssetPaths)
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

    internal static bool ShouldPublishUnifiedGitHub(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        bool moduleSelected)
    {
        if (request.ModuleRunMode.HasValue &&
            request.ModuleRunMode.Value != ConfigurationGateMode.Publish)
        {
            return false;
        }

        if (request.ToolsOnly)
            return false;

        if (request.PackagesOnly && request.PublishProjectGitHub == true)
            return false;

        if (moduleSelected && spec.Module is not null)
        {
            var packagePublishingRequested =
                !request.ModuleOnly &&
                ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
                 (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true);
            if (ResolveModuleRunMode(spec.Module!, request, packagePublishingRequested) != ConfigurationGateMode.Publish)
                return false;
        }

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
        var manifestArtifacts = new List<PowerForgeWingetManifestArtifact>();
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
                .Select(installer => ResolveWingetInstallerEntry(
                    installer,
                    winget,
                    package,
                    result.ReleaseAssetEntries,
                    result.ToolGitHubReleases.Concat(result.ToolGitHubReleasePlans).ToArray()))
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
            var yaml = WingetManifestWriter.Build(winget, package, packageVersion!, installerEntries);
            File.WriteAllText(manifestPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            manifestPaths.Add(manifestPath);
            manifestArtifacts.Add(new PowerForgeWingetManifestArtifact
            {
                PackageIdentifier = package.PackageIdentifier,
                PackageVersion = packageVersion!,
                ManifestPath = manifestPath,
                InstallerUrls = installerEntries
                    .Select(entry => entry.InstallerUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        }

        result.WingetManifestPaths = manifestPaths.ToArray();
        result.WingetManifests = manifestArtifacts.ToArray();
    }

    private void SubmitWingetOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory,
        PowerForgeReleaseResult result)
    {
        var winget = spec.Winget;
        if (winget is null)
        {
            if (request.SubmitWinget == true)
                throw new InvalidOperationException("Winget submission was requested, but the release config does not define a Winget section.");
            return;
        }

        var service = new WingetSubmissionService(_logger);
        var plan = service.Plan(winget, result.WingetManifests, configDirectory, request);
        result.WingetSubmissionPlan = plan;
        if (!plan.Enabled)
            return;

        var submission = _submitWinget(plan);
        result.WingetSubmission = submission;
        if (!submission.Succeeded)
        {
            result.Success = false;
            result.ErrorMessage = submission.ErrorMessage ?? "Winget submission failed.";
        }
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
        string? sharedReleaseVersion,
        IPowerForgeReleaseProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var gitHub = spec.GitHub ?? throw new InvalidOperationException("Unified GitHub release options were not configured.");
        var recoveryConfigurationError = IsVerifiedGitHubRecoveryRequested(gitHub)
            ? GetVerifiedGitHubRecoveryConfigurationError(gitHub)
            : null;
        if (recoveryConfigurationError is not null)
        {
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Success = false,
                ErrorMessage = recoveryConfigurationError
            };
        }
        var version = ResolveUnifiedReleaseVersion(gitHub, result, sharedReleaseVersion);
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

        var recoveryError = RestorePublishedNuGetAssetsForGitHubRecovery(
            spec,
            gitHub,
            result,
            resolved.Owner ?? string.Empty,
            resolved.Repository ?? string.Empty,
            version!,
            cancellationToken,
            out var recoveredPublishedNuGetAssets,
            out var recoveredPublishedPackageReleaseZips);
        if (recoveryError is not null)
            return recoveryError;

        recoveryError = RestorePublishedModuleAssetsForGitHubRecovery(
            gitHub,
            result,
            resolved.Owner ?? string.Empty,
            resolved.Repository ?? string.Empty,
            version!,
            cancellationToken,
            out var recoveredPublishedModuleAssets);
        if (recoveryError is not null)
            return recoveryError;
        if (recoveredPublishedNuGetAssets.Length > 0 ||
            recoveredPublishedPackageReleaseZips.Length > 0 ||
            recoveredPublishedModuleAssets.Length > 0)
            RewriteReleaseSummaryFiles(result);

        var expectedAssets = result.ReleaseAssets
            .Concat(new[]
            {
                result.ReleaseManifestPath,
                result.ReleaseChecksumsPath
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => path!)
            .ToArray();
        var missingAssets = expectedAssets
            .Where(static path => !File.Exists(path))
            .ToArray();
        if (missingAssets.Length > 0)
        {
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = resolved.Owner ?? string.Empty,
                Repository = resolved.Repository ?? string.Empty,
                Version = version!,
                Success = false,
                ErrorMessage = "Checkpointed unified GitHub release assets are missing: " +
                               string.Join(", ", missingAssets)
            };
        }

        var duplicateAssetNames = expectedAssets
            .GroupBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToArray();
        if (duplicateAssetNames.Length > 0)
        {
            var collisions = duplicateAssetNames
                .Select(group => $"{group.Key}: {string.Join(", ", group)}");
            return new PowerForgeUnifiedGitHubReleaseResult
            {
                Owner = resolved.Owner ?? string.Empty,
                Repository = resolved.Repository ?? string.Empty,
                Version = version!,
                Success = false,
                ErrorMessage = "Unified GitHub release assets must have unique file names: " +
                               string.Join("; ", collisions)
            };
        }

        var assets = expectedAssets;
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
            var publishResult = _publishGitHubRelease(
                new GitHubReleasePublishRequest
                {
                    Owner = resolved.Owner!,
                    Repository = resolved.Repository!,
                    Token = resolved.Token!,
                    TagName = tagName,
                    ReleaseName = releaseName,
                    Commitish = gitHub.Commitish,
                    ExpectedTagCommitSha = gitHub.Commitish,
                    GenerateReleaseNotes = gitHub.GenerateReleaseNotes,
                    IsPreRelease = gitHub.IsPreRelease,
                    ReuseExistingReleaseOnConflict = gitHub.ReuseExistingRelease,
                    RequireExpectedExistingRelease = gitHub.RequireExpectedExistingRelease,
                    ExpectedExistingReleaseId = gitHub.ExpectedExistingReleaseId,
                    RequirePublishedStableRelease = gitHub.RequirePublishedStableRelease,
                    ReplaceExistingAssets = gitHub.ReuseExistingRelease && gitHub.ReplaceExistingAssets,
                    AssetFilePaths = assets,
                    Progress = progress is IPowerForgeReleaseProgressReporterV2 detailedProgress
                        ? new GitHubReleaseProgressAdapter(detailedProgress)
                        : null
                },
                cancellationToken);

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
                SkippedExistingAssets = publishResult.SkippedExistingAssets?.ToArray() ?? Array.Empty<string>(),
                ReplacedExistingAssets = publishResult.ReplacedExistingAssets?.ToArray() ?? Array.Empty<string>(),
                UploadedAssets = publishResult.UploadedAssets?.ToArray() ?? Array.Empty<string>(),
                RecoveredPublishedNuGetAssets = recoveredPublishedNuGetAssets,
                RecoveredPublishedPackageReleaseZips = recoveredPublishedPackageReleaseZips,
                RecoveredPublishedModuleAssets = recoveredPublishedModuleAssets
            };
        }
        catch (OperationCanceledException)
        {
            throw;
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
        PowerForgeToolReleaseResult result,
        CancellationToken cancellationToken)
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
                assets,
                cancellationToken));
        }

        return results.ToArray();
    }

    private PowerForgeToolGitHubReleaseResult[] PublishDotNetToolGitHubReleases(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        DotNetPublishPlan plan,
        DotNetPublishResult result,
        string? sharedReleaseVersion,
        CancellationToken cancellationToken)
    {
        var gitHub = spec.Tools?.GitHub ?? new PowerForgeToolReleaseGitHubOptions();
        var resolved = ResolveGitHubConfiguration(spec, gitHub, configDirectory);
        if (resolved.Error is not null)
            return new[] { resolved.Error };

        var globalAssets = new[]
        {
            result.ManifestJsonPath,
            result.ManifestTextPath,
            result.RunReportPath,
            result.RunReportMarkdownPath
        };

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
            if (!TryBuildDotNetGitHubRunnableAssets(
                    plan,
                    target,
                    result,
                    out List<string> runnableAssets,
                    out string checksumDirectory,
                    out string safeTarget,
                    out string? artefactError))
            {
                releases.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = target.Name,
                    Version = version ?? string.Empty,
                    Success = false,
                    ErrorMessage = artefactError ?? $"No runnable release artifact was produced for DotNet publish target '{target.Name}'."
                });
                continue;
            }
            assets.AddRange(runnableAssets);

            assets.AddRange(
                globalAssets
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!));

            assets.AddRange(
                GetDotNetGitHubConfigurationAssets(
                    plan,
                    Path.Combine(checksumDirectory, safeTarget + ".configuration-assets")));

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

            string releaseChecksumsPath = ModulePublisher.WriteGitHubChecksumCatalog(
                Path.Combine(checksumDirectory, safeTarget + ".SHA256SUMS.txt"),
                uniqueAssets);
            string[] releaseAssets = uniqueAssets
                .Concat(new[] { releaseChecksumsPath })
                .ToArray();

            releases.Add(PublishGitHubRelease(
                resolved.Owner!,
                resolved.Repository!,
                resolved.Token!,
                gitHub,
                target.Name,
                version!,
                releaseAssets,
                cancellationToken));
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
        string[] assets,
        CancellationToken cancellationToken)
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
            var publishResult = _publishGitHubRelease(
                new GitHubReleasePublishRequest
                {
                    Owner = owner,
                    Repository = repository,
                    Token = token,
                    TagName = tagName,
                    ReleaseName = releaseName,
                    GenerateReleaseNotes = gitHub.GenerateReleaseNotes,
                    IsPreRelease = gitHub.IsPreRelease,
                    ReuseExistingReleaseOnConflict = true,
                    ReplaceExistingAssets = gitHub.ReplaceExistingAssets,
                    AssetFilePaths = assets
                },
                cancellationToken);

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
                SkippedExistingAssets = publishResult.SkippedExistingAssets?.ToArray() ?? Array.Empty<string>(),
                ReplacedExistingAssets = publishResult.ReplacedExistingAssets?.ToArray() ?? Array.Empty<string>()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
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

        if (!string.IsNullOrWhiteSpace(target.Version))
            return target.Version;

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

    private static bool DotNetToolsConfigExists(PowerForgeToolReleaseSpec tools, string releaseConfigPath)
    {
        if (string.IsNullOrWhiteSpace(tools.DotNetPublishConfigPath))
            return false;

        var releaseConfigDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.GetFullPath(Path.IsPathRooted(tools.DotNetPublishConfigPath)
            ? tools.DotNetPublishConfigPath!
            : Path.Combine(releaseConfigDirectory, tools.DotNetPublishConfigPath!));
        return File.Exists(configPath);
    }

    private static void ApplyDotNetPublishProfileOverride(PowerForgeToolReleaseSpec tools)
    {
        if (tools.DotNetPublish is null || string.IsNullOrWhiteSpace(tools.DotNetPublishProfile))
            return;

        tools.DotNetPublish.Profile = tools.DotNetPublishProfile!.Trim();
    }

    private static (DotNetPublishSpec Spec, string SourceConfigPath) LoadDotNetToolsSpec(PowerForgeToolReleaseSpec tools, string releaseConfigPath)
    {
        if (tools.DotNetPublish is not null && !string.IsNullOrWhiteSpace(tools.DotNetPublishConfigPath))
            throw new InvalidOperationException("Tools.DotNetPublish and Tools.DotNetPublishConfigPath are mutually exclusive.");

        if (tools.DotNetPublish is not null)
        {
            ApplyDotNetPublishProfileOverride(tools);
            PowerForgeReleaseConfigurationSecretValidator.Validate(tools.DotNetPublish);

            return (tools.DotNetPublish, releaseConfigPath);
        }

        if (string.IsNullOrWhiteSpace(tools.DotNetPublishConfigPath))
            throw new InvalidOperationException("DotNet publish tool workflow requires Tools.DotNetPublish or Tools.DotNetPublishConfigPath.");

        var releaseConfigDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.GetFullPath(Path.IsPathRooted(tools.DotNetPublishConfigPath)
            ? tools.DotNetPublishConfigPath!
            : Path.Combine(releaseConfigDirectory, tools.DotNetPublishConfigPath!));

        if (!File.Exists(configPath))
        {
            if (StagedDotNetPublishConfigurationFileName.IsMatch(Path.GetFileName(configPath)))
            {
                throw new InvalidOperationException(
                    $"Authorized DotNet publish configuration evidence was not found: {configPath}");
            }
            throw new FileNotFoundException($"DotNet publish config not found: {configPath}", configPath);
        }

        var json = File.ReadAllText(configPath);
        PowerForgeReleaseConfigurationSecretValidator.ValidateJson(json);
        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, DotNetToolsJsonOptions)
            ?? throw new InvalidOperationException($"DotNet publish config could not be deserialized: {configPath}");

        if (!string.IsNullOrWhiteSpace(tools.DotNetPublishProfile))
            spec.Profile = tools.DotNetPublishProfile!.Trim();

        PowerForgeReleaseConfigurationSecretValidator.Validate(spec);

        return (spec, configPath);
    }

    private static string ResolveDotNetPlanningConfigurationPath(
        string loadedConfigurationPath,
        string sourceReleaseConfigurationPath,
        bool inlineConfiguration)
    {
        if (inlineConfiguration)
            return Path.GetFullPath(sourceReleaseConfigurationPath);

        string loadedPath = Path.GetFullPath(loadedConfigurationPath);
        Match stagedMatch = StagedDotNetPublishConfigurationFileName.Match(Path.GetFileName(loadedPath));
        if (!stagedMatch.Success)
            return loadedPath;

        PowerForgeReleaseSpec sourceSpec = LoadConfiguration(sourceReleaseConfigurationPath);
        string? sourceReference = sourceSpec.Tools?.DotNetPublishConfigPath;
        if (string.IsNullOrWhiteSpace(sourceReference))
            throw new InvalidOperationException(
                "The source release configuration no longer identifies the staged DotNet publish configuration.");
        string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceReleaseConfigurationPath))
            ?? Directory.GetCurrentDirectory();
        string sourcePath = Path.GetFullPath(Path.IsPathRooted(sourceReference)
            ? sourceReference
            : Path.Combine(sourceDirectory, sourceReference));
        if (!File.Exists(sourcePath) || !string.Equals(
                AppleNotarizationService.ComputeFileSha256(sourcePath),
                stagedMatch.Groups["sha256"].Value,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The staged DotNet publish configuration does not match the caller-owned source configuration.");
        }

        return sourcePath;
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
        DotNetPublishPlan plan = new DotNetPublishPipelineRunner(logger).Plan(spec, configPath);
        string releaseConfigPath = Path.GetFullPath(request.ConfigPath.Trim().Trim('"'));
        plan.ConfigurationInputPaths = plan.ConfigurationInputPaths
            .Concat(new[] { releaseConfigPath })
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        BindGeneratedConfigurationInput(plan, request);
        request.DotNetPublishPlan = plan;
        return plan;
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
                var hasRequestedSignProfile = !string.IsNullOrWhiteSpace(request.SignProfile);
                var baseSign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    spec.SigningProfiles,
                    hasRequestedSignProfile ? request.SignProfile : target.Publish.SignProfile,
                    hasRequestedSignProfile ? null : target.Publish.Sign,
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
                var hasRequestedSignProfile = !string.IsNullOrWhiteSpace(request.SignProfile);
                var baseSign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    spec.SigningProfiles,
                    hasRequestedSignProfile ? request.SignProfile : installer.SignProfile,
                    hasRequestedSignProfile ? null : installer.Sign,
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
        spec.Outputs.RunReportMarkdownPath = CombineOutputRoot(
            normalizedRoot,
            string.IsNullOrWhiteSpace(spec.Outputs.RunReportMarkdownPath)
                ? DefaultDotNetRunReportMarkdownTemplate
                : spec.Outputs.RunReportMarkdownPath!);
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
        string? sharedReleaseVersion,
        IReadOnlyCollection<string>? additionalAssetPaths = null)
    {
        var assets = new List<PowerForgeReleaseAssetEntry>();

        assets.AddRange(
            (result.ModuleAssets ?? Array.Empty<string>())
            .SelectMany(path => CreateModuleAssetEntries(
                path,
                result.ModulePlan,
                result.ModuleProducedAssets)));

        assets.AddRange(
            (result.Packages?.Result.Release?.Projects ?? new List<DotNetRepositoryProjectResult>())
            .SelectMany(project => CreatePackageAssetEntries(project)));

        assets.AddRange(
            (result.Tools?.Artefacts ?? Array.Empty<PowerForgeToolReleaseArtifactResult>())
            .SelectMany(artifact => CreateLegacyToolAssetEntries(artifact)));

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
                Style = item.Build.Style.ToString(),
                IsFinalPackageOutput = true
            }));

        assets.AddRange(
            (result.DotNetTools?.StorePackages ?? Array.Empty<DotNetPublishStorePackageResult>())
            .SelectMany(storePackage => CreateDotNetStorePackageEntries(
                storePackage,
                dotNetPlan,
                sharedReleaseVersion)));

        assets.AddRange(
            new[]
            {
                result.DotNetTools?.ManifestJsonPath,
                result.DotNetTools?.ManifestTextPath,
                result.DotNetTools?.ChecksumsPath,
                result.DotNetTools?.RunReportPath,
                result.DotNetTools?.RunReportMarkdownPath
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new PowerForgeReleaseAssetEntry
            {
                Path = path!,
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "DotNetPublish"
            }));

        assets.AddRange(
            (additionalAssetPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new PowerForgeReleaseAssetEntry
            {
                Path = path,
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "ReleaseOutputs",
                IsFinalPackageOutput = false
            }));

        assets.AddRange(CreateConfigurationAssetEntries(
            dotNetPlan,
            result.DotNetTools?.ChecksumsPath));

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
                Version = project.NewVersion,
                IsFinalPackageOutput = true
            };
        }

        foreach (var symbolPackage in project.SymbolPackages.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = symbolPackage,
                Category = PowerForgeReleaseAssetCategory.Package,
                Source = "Packages",
                Target = project.ProjectName,
                PackageId = project.PackageId,
                Version = project.NewVersion,
                IsFinalPackageOutput = false
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
                Version = project.NewVersion,
                IsFinalPackageOutput = true
            };
        }
    }

    internal static IEnumerable<PowerForgeReleaseAssetEntry> CreateModuleAssetEntries(
        string path,
        PowerForgeModuleReleasePlanSummary? plan = null,
        IReadOnlyCollection<string>? producedArtifactPaths = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        var produced = producedArtifactPaths is null
            ? null
            : new HashSet<string>(
                producedArtifactPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            var fullPath = Path.GetFullPath(path);
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = fullPath,
                Category = PowerForgeReleaseAssetCategory.Module,
                Source = "Module",
                Version = ResolveModuleReleaseVersion(plan),
                IsFinalPackageOutput = produced?.Contains(fullPath) == true &&
                                       IsFinalPowerShellModulePackage(fullPath, plan)
            };
            yield break;
        }

        if (!Directory.Exists(path))
            yield break;

        foreach (var file in Directory
            .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
            .Where(file => IsModuleArtifactForResolvedVersion(file, plan))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(file);
            yield return new PowerForgeReleaseAssetEntry
            {
                Path = fullPath,
                Category = PowerForgeReleaseAssetCategory.Module,
                Source = "Module",
                Version = ResolveModuleReleaseVersion(plan),
                IsFinalPackageOutput = produced?.Contains(fullPath) == true &&
                                       IsFinalPowerShellModulePackage(fullPath, plan)
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

    private static WingetManifestInstallerEntry ResolveWingetInstallerEntry(
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

        return new WingetManifestInstallerEntry
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
        => ResolveDotNetTargetVersion(artifact.Target, plan, sharedReleaseVersion);

    private static string? ResolveDotNetTargetVersion(string targetName, DotNetPublishPlan? plan, string? sharedReleaseVersion)
    {
        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return sharedReleaseVersion;

        if (plan?.Targets is null)
            return null;

        var target = plan.Targets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return null;

        return string.IsNullOrWhiteSpace(target.Version)
            ? null
            : target.Version;
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
                SymbolPackages = project.SymbolPackages.ToArray(),
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
            appleApps = BuildAppleAppsManifestSection(result.AppleAppPlan, result.AppleApps),
            githubReleases = result.ToolGitHubReleases,
            unifiedGithubRelease = result.UnifiedGitHubRelease
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(manifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static object? BuildAppleAppsManifestSection(
        PowerForgeAppleReleasePlan? plan,
        PowerForgeAppleAppReleaseResult[] results)
    {
        if (plan is null && (results is null || results.Length == 0))
            return null;

        return new
        {
            plan = plan is null ? null : new
            {
                plan.ProjectRoot,
                plan.Configuration,
                plan.Archive,
                plan.Upload,
                plan.SyncScreenshots,
                plan.SyncMetadata,
                plan.SyncAppInfo,
                plan.CheckGovernance,
                plan.PrepareDistribution,
                plan.SelectBuildForDistribution,
                plan.AllowUnprocessedDistributionBuild,
                plan.ScreenshotConfigPath,
                plan.ScreenshotConfigPaths,
                plan.MetadataConfigPath,
                plan.MetadataConfigPaths,
                plan.AppInfoConfigPath,
                plan.AppInfoConfigPaths,
                plan.GovernanceConfigPath,
                plan.GovernanceConfigPaths,
                plan.ReplaceScreenshots,
                plan.CheckReleaseReadiness,
                plan.DistributeTestFlight,
                plan.TestFlightBetaGroupIds,
                plan.TestFlightBetaGroupNames,
                TestFlightTesterCount = plan.TestFlightTesterEmails.Length,
                plan.CreateMissingTestFlightTesters,
                plan.AllowUnprocessedTestFlightBuild,
                plan.SubmitTestFlightBetaReview,
                plan.SubmitForReview,
                plan.AllowUnselectedReviewBuild,
                plan.AllowUnprocessedReviewBuild,
                plan.SkipReviewReadinessCheck,
                plan.AllowReviewSubmissionWhenNotReady,
                plan.ReleaseApprovedVersion,
                plan.AllowNonPendingDeveloperRelease,
                plan.XcodeBuildExecutable,
                plan.AllowProvisioningUpdates,
                plan.ManageAppVersionAndBuildNumber,
                plan.UploadSymbols,
                plan.GenerateAppStoreInformation,
                plan.SigningStyle,
                AppStoreConnectApiKeyConfigured = !string.IsNullOrWhiteSpace(plan.AppStoreConnectApiKeyPath),
                apps = plan.Apps.Select(app => new
                {
                    app.Name,
                    app.BundleId,
                    Platform = app.Platform.ToString(),
                    ArchiveVariant = app.ArchiveVariant.ToString(),
                    app.AppStoreConnectAppId,
                    app.ProjectPath,
                    app.IsWorkspace,
                    app.Scheme,
                    app.Configuration,
                    app.Destination,
                    app.ArchivePath,
                    app.ExportPath,
                    app.TeamId,
                    app.Upload,
                    app.VersionUpdateRequested,
                    app.MarketingVersion,
                    app.BuildNumber,
                    BuildNumberPolicy = app.BuildNumberPolicy.ToString()
                }).ToArray()
            },
            results = (results ?? Array.Empty<PowerForgeAppleAppReleaseResult>()).Select(result => new
            {
                app = result.Plan.Name,
                result.Success,
                result.ErrorMessage,
                versionUpdate = result.VersionUpdate is null ? null : new
                {
                    result.VersionUpdate.ProjectFilePath,
                    result.VersionUpdate.Changed,
                    result.VersionUpdate.WhatIf,
                    before = new
                    {
                        result.VersionUpdate.Before.MarketingVersion,
                        result.VersionUpdate.Before.BuildNumber
                    },
                    after = new
                    {
                        result.VersionUpdate.After.MarketingVersion,
                        result.VersionUpdate.After.BuildNumber
                    }
                },
                archive = result.Archive is null ? null : new
                {
                    result.Archive.ArchivePath,
                    result.Archive.Destination,
                    result.Archive.Succeeded,
                    ExitCode = result.Archive.ProcessResult.ExitCode
                },
                upload = result.Upload is null ? null : new
                {
                    result.Upload.ArchivePath,
                    result.Upload.ExportPath,
                    result.Upload.ExportOptionsPlistPath,
                    result.Upload.Succeeded,
                    ExitCode = result.Upload.ProcessResult.ExitCode
                },
                distribution = result.Distribution is null ? null : new
                {
                    result.Distribution.AppId,
                    result.Distribution.VersionString,
                    result.Distribution.BuildNumber,
                    Platform = result.Distribution.Platform.ToString(),
                    VersionId = result.Distribution.Version?.Id,
                    BuildId = result.Distribution.Build?.Id,
                    result.Distribution.CreatedVersion,
                    result.Distribution.SelectedBuild,
                    result.Distribution.PreviousBuildId,
                    ScreenshotSetCount = result.Distribution.Screenshots?.ScreenshotSets.Length ?? 0,
                    MetadataUpdatedFields = result.Distribution.Metadata?.UpdatedFields ?? Array.Empty<string>(),
                    AppInfoMetadataUpdatedFields = result.Distribution.AppInfoMetadataResults
                        .SelectMany(metadata => metadata.UpdatedFields)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    ReadinessReady = result.Distribution.Readiness?.IsReady,
                    ReadinessChecks = result.Distribution.Readiness?.Checks.Select(check => new
                    {
                        check.Name,
                        check.Passed,
                        check.Message
                    }).ToArray(),
                    result.Distribution.Messages
                },
                testFlight = result.TestFlight is null ? null : new
                {
                    BuildId = result.TestFlight.Build.Id,
                    BuildNumber = result.TestFlight.Build.Version,
                    BetaGroups = result.TestFlight.BetaGroups.Select(group => new
                    {
                        group.Id,
                        group.Name,
                        group.PublicLinkEnabled
                    }).ToArray(),
                    TesterCount = result.TestFlight.Testers.Length,
                    result.TestFlight.Messages
                },
                testFlightBetaReviewSubmission = result.TestFlightBetaReviewSubmission is null ? null : new
                {
                    result.TestFlightBetaReviewSubmission.AppId,
                    result.TestFlightBetaReviewSubmission.VersionString,
                    result.TestFlightBetaReviewSubmission.BuildNumber,
                    Platform = result.TestFlightBetaReviewSubmission.Platform.ToString(),
                    BuildId = result.TestFlightBetaReviewSubmission.Build.Id,
                    SubmissionId = result.TestFlightBetaReviewSubmission.Submission.Id,
                    result.TestFlightBetaReviewSubmission.Submission.BetaReviewState,
                    result.TestFlightBetaReviewSubmission.Submission.SubmittedDate,
                    result.TestFlightBetaReviewSubmission.Messages
                },
                reviewSubmission = result.ReviewSubmission is null ? null : new
                {
                    result.ReviewSubmission.AppId,
                    result.ReviewSubmission.VersionString,
                    result.ReviewSubmission.BuildNumber,
                    Platform = result.ReviewSubmission.Platform.ToString(),
                    VersionId = result.ReviewSubmission.Version.Id,
                    BuildId = result.ReviewSubmission.Build?.Id,
                    ReviewSubmissionId = result.ReviewSubmission.ReviewSubmission.Id,
                    result.ReviewSubmission.ReviewSubmission.IsSubmitted,
                    result.ReviewSubmission.ReviewSubmission.State,
                    ReviewSubmissionItemId = result.ReviewSubmission.ReviewSubmissionItem?.Id,
                    ReadinessReady = result.ReviewSubmission.Readiness?.IsReady,
                    result.ReviewSubmission.Messages
                },
                versionRelease = result.VersionRelease is null ? null : new
                {
                    result.VersionRelease.AppId,
                    result.VersionRelease.VersionString,
                    Platform = result.VersionRelease.Platform.ToString(),
                    VersionId = result.VersionRelease.Version.Id,
                    result.VersionRelease.Version.AppStoreState,
                    result.VersionRelease.Version.AppVersionState,
                    ReleaseRequestId = result.VersionRelease.ReleaseRequest.Id,
                    result.VersionRelease.Messages
                }
            }).ToArray()
        };
    }

    internal static void WriteReleaseChecksums(PowerForgeReleaseResult result, string checksumsPath)
    {
        var checksumInputs = new List<string>(result.ReleaseAssets);
        if (!string.IsNullOrWhiteSpace(result.ReleaseManifestPath) && File.Exists(result.ReleaseManifestPath))
            checksumInputs.Add(result.ReleaseManifestPath!);

        var uniqueChecksumInputs = checksumInputs
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string fullChecksumsPath = Path.GetFullPath(checksumsPath);
        string? checksumInputCollision = uniqueChecksumInputs.FirstOrDefault(path =>
            string.Equals(Path.GetFullPath(path), fullChecksumsPath, StringComparison.OrdinalIgnoreCase));
        if (checksumInputCollision is not null)
        {
            throw new InvalidOperationException(
                $"Unified release checksum destination collides with release input '{checksumInputCollision}'.");
        }

        string[] collidingAssetNames = uniqueChecksumInputs
            .Concat(new[] { fullChecksumsPath })
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key ?? string.Empty)
            .ToArray();
        if (collidingAssetNames.Length > 0)
        {
            throw new InvalidOperationException(
                "Unified GitHub release assets must have unique file names before checksums are written: " +
                string.Join(", ", collidingAssetNames));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullChecksumsPath)!);
        var lines = uniqueChecksumInputs
            .Select(path => $"{ComputeSha256(path!)} *{Path.GetFileName(path!)}")
            .ToArray();
        File.WriteAllLines(fullChecksumsPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
            tools.RunReportMarkdownPath,
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

    internal static string SanitizeStageEntryName(string value)
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

    private static string? NormalizeAppleConfiguration(string? configuration)
    {
        return string.IsNullOrWhiteSpace(configuration) ? null : configuration!.Trim();
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
        if (request.EnableSigning == false)
        {
            packages.SignAssemblies = false;
            packages.SignPackages = false;
        }
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
        return request.EnableSigning.HasValue
            || !string.IsNullOrWhiteSpace(request.SignProfile)
            || HasExplicitSigningValueOverrides(request);
    }

    private static bool HasExplicitSigningValueOverrides(PowerForgeReleaseRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.SignToolPath)
            || !string.IsNullOrWhiteSpace(request.SignThumbprint)
            || !string.IsNullOrWhiteSpace(request.SignSubjectName)
            || request.SignTimeoutSeconds.HasValue
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
        else if (!string.IsNullOrWhiteSpace(request.SignSubjectName))
            sign.Thumbprint = null;
        if (!string.IsNullOrWhiteSpace(request.SignSubjectName))
            sign.SubjectName = request.SignSubjectName!.Trim();
        if (request.SignOnMissingTool.HasValue)
            sign.OnMissingTool = request.SignOnMissingTool.Value;
        if (request.SignOnFailure.HasValue)
            sign.OnSignFailure = request.SignOnFailure.Value;
        if (request.SignTimeoutSeconds.HasValue)
            sign.TimeoutSeconds = Math.Max(1, request.SignTimeoutSeconds.Value);
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

    internal static string BuildModuleFailureMessage(string scriptPath, ModuleBuildHostExecutionResult result)
    {
        var heading = $"Module release workflow failed while executing '{scriptPath}' (exit code {result.ExitCode}).";
        var detail = result.FailureMessage is string structuredFailure &&
                     !string.IsNullOrWhiteSpace(structuredFailure)
            ? structuredFailure
            : !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : NormalizeModuleFailureOutput(result.StandardOutput);

        if (string.IsNullOrWhiteSpace(detail))
            return heading + " See the captured module log for details.";

        return heading + Environment.NewLine + detail!.Trim();
    }

    private static string? NormalizeModuleFailureOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var plain = AnsiEscapeSequence
            .Replace(output, string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var normalized = string.Join(
            Environment.NewLine,
            plain.Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0));

        if (normalized.Length == 0)
            return null;

        const int maxCharacters = 4_000;
        if (normalized.Length <= maxCharacters)
            return normalized;

        return "…" + normalized.Substring(normalized.Length - (maxCharacters - 1));
    }
}
