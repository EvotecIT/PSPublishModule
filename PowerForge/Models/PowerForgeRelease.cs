using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Unified repository release configuration that can drive package and tool outputs from one JSON file.
/// </summary>
internal sealed class PowerForgeReleaseSpec
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public PowerForgeModuleReleaseOptions? Module { get; set; }

    public ProjectBuildConfiguration? Packages { get; set; }

    public PowerForgeToolReleaseSpec? Tools { get; set; }

    public PowerForgeAppleReleaseOptions? AppleApps { get; set; }

    public PowerForgeWorkspaceValidationOptions? WorkspaceValidation { get; set; }

    public PowerForgeReleaseOutputsOptions Outputs { get; set; } = new();

    public PowerForgeReleaseGitHubOptions? GitHub { get; set; }

    public PowerForgeReleaseWingetOptions? Winget { get; set; }
}

/// <summary>
/// Host-facing request for executing a unified release workflow.
/// </summary>
internal sealed class PowerForgeReleaseRequest
{
    public string ConfigPath { get; set; } = string.Empty;

    public bool PlanOnly { get; set; }

    public bool ValidateOnly { get; set; }

    public bool PackagesOnly { get; set; }

    public bool ModuleOnly { get; set; }

    public bool ToolsOnly { get; set; }

    public bool? PublishNuget { get; set; }

    public bool? PublishProjectGitHub { get; set; }

    public bool? PublishToolGitHub { get; set; }

    public string? Configuration { get; set; }

    public string? ModuleFramework { get; set; }

    public ConfigurationGateMode? ModuleRunMode { get; set; }

    public bool? ModuleNoDotnetBuild { get; set; }

    public string? ModuleVersion { get; set; }

    public string? ModulePreReleaseTag { get; set; }

    public bool? ModuleNoSign { get; set; }

    public bool? ModuleSignModule { get; set; }

    public int? ModuleTimeoutSeconds { get; set; }

    public string? ModuleCertificateThumbprint { get; set; }

    public bool? ModuleSignIncludeBinaries { get; set; }

    public bool? ModuleSignIncludeInternals { get; set; }

    public bool? ModuleSignIncludeExe { get; set; }

    public string? ModuleDiagnosticsBaselinePath { get; set; }

    public bool? ModuleGenerateDiagnosticsBaseline { get; set; }

    public bool? ModuleUpdateDiagnosticsBaseline { get; set; }

    public bool? ModuleFailOnNewDiagnostics { get; set; }

    public string? ModuleFailOnDiagnosticsSeverity { get; set; }

    public bool SkipRestore { get; set; }

    public bool SkipBuild { get; set; }

    public bool SkipWorkspaceValidation { get; set; }

    public string? WorkspaceConfigPath { get; set; }

    public string? WorkspaceProfile { get; set; }

    public string? WorkspaceTestimoXRoot { get; set; }

    public string[] WorkspaceEnableFeatures { get; set; } = Array.Empty<string>();

    public string[] WorkspaceDisableFeatures { get; set; } = Array.Empty<string>();

    public string? OutputRoot { get; set; }

    public string? StageRoot { get; set; }

    public string? ManifestJsonPath { get; set; }

    public bool AllowOutputOutsideProjectRoot { get; set; }

    public bool AllowManifestOutsideProjectRoot { get; set; }

    public string? ChecksumsPath { get; set; }

    public bool SkipReleaseChecksums { get; set; }

    public bool? KeepSymbols { get; set; }

    public bool? EnableSigning { get; set; }

    public string? SignProfile { get; set; }

    public string? SignToolPath { get; set; }

    public string? SignThumbprint { get; set; }

    public string? SignSubjectName { get; set; }

    public DotNetPublishPolicyMode? SignOnMissingTool { get; set; }

    public DotNetPublishPolicyMode? SignOnFailure { get; set; }

    public int? SignTimeoutSeconds { get; set; }

    public string? SignTimestampUrl { get; set; }

    public string? SignDescription { get; set; }

    public string? SignUrl { get; set; }

    public string? SignCsp { get; set; }

    public string? SignKeyContainer { get; set; }

    public string? PackageSignThumbprint { get; set; }

    public string? PackageSignStore { get; set; }

    public string? PackageSignTimestampUrl { get; set; }

    public bool? SubmitWinget { get; set; }

    public PowerForgeWingetSubmissionMode? WingetSubmitMode { get; set; }

    public string? WingetSubmitToolPath { get; set; }

    public string? WingetSubmitToken { get; set; }

    public string? WingetSubmitTokenFilePath { get; set; }

    public string? WingetSubmitTokenEnvName { get; set; }

    public string? WingetSubmitPrTitle { get; set; }

    public bool? WingetSubmitNoOpen { get; set; }

    public bool? WingetSubmitReplace { get; set; }

    public string? WingetSubmitReplaceVersion { get; set; }

    public bool? WingetSubmitAllowInteractiveAuthentication { get; set; }

    public int? WingetSubmitTimeoutSeconds { get; set; }

    public Dictionary<string, string> InstallerMsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string[] Targets { get; set; } = Array.Empty<string>();

    public string[] Runtimes { get; set; } = Array.Empty<string>();

    public string[] Frameworks { get; set; } = Array.Empty<string>();

    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    public PowerForgeToolReleaseFlavor[] Flavors { get; set; } = Array.Empty<PowerForgeToolReleaseFlavor>();

    public PowerForgeReleaseToolOutputKind[] ToolOutputs { get; set; } = Array.Empty<PowerForgeReleaseToolOutputKind>();

    public PowerForgeReleaseToolOutputKind[] SkipToolOutputs { get; set; } = Array.Empty<PowerForgeReleaseToolOutputKind>();

    public PowerForgeAppleReleaseAction AppleAction { get; set; } = PowerForgeAppleReleaseAction.Configured;

    public bool AppleActionConfirmed { get; set; }

    public bool? AppleResume { get; set; }

    public bool? AppleWaitForProcessing { get; set; }

    public int? AppleProcessingTimeoutSeconds { get; set; }

    public int? ApplePollIntervalSeconds { get; set; }

    public bool AppleSummaryOnly { get; set; }
}

/// <summary>
/// Aggregate result for a unified release run.
/// </summary>
internal sealed class PowerForgeReleaseResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string ConfigPath { get; set; } = string.Empty;

    public PowerForgeModuleReleasePlanSummary? ModulePlan { get; set; }

    public ModuleBuildHostExecutionResult? Module { get; set; }

    public string[] ModuleAssets { get; set; } = Array.Empty<string>();

    public ProjectBuildHostExecutionResult? Packages { get; set; }

    public PowerForgeToolReleasePlan? ToolPlan { get; set; }

    public PowerForgeToolReleaseResult? Tools { get; set; }

    public DotNetPublishPlan? DotNetToolPlan { get; set; }

    public DotNetPublishResult? DotNetTools { get; set; }

    public PowerForgeAppleReleasePlan? AppleAppPlan { get; set; }

    public PowerForgeAppleAppReleaseResult[] AppleApps { get; set; } = Array.Empty<PowerForgeAppleAppReleaseResult>();

    public PowerForgeAppleReleaseReceipt? AppleReceipt { get; set; }

    public WorkspaceValidationPlan? WorkspaceValidationPlan { get; set; }

    public WorkspaceValidationResult? WorkspaceValidation { get; set; }

    public PowerForgeToolGitHubReleaseResult[] ToolGitHubReleases { get; set; } = Array.Empty<PowerForgeToolGitHubReleaseResult>();

    public string[] ReleaseAssets { get; set; } = Array.Empty<string>();

    public PowerForgeReleaseAssetEntry[] ReleaseAssetEntries { get; set; } = Array.Empty<PowerForgeReleaseAssetEntry>();

    public PowerForgeUnifiedGitHubReleaseResult? UnifiedGitHubRelease { get; set; }

    public string? ReleaseManifestPath { get; set; }

    public string? ReleaseChecksumsPath { get; set; }

    public string[] WingetManifestPaths { get; set; } = Array.Empty<string>();

    public PowerForgeWingetManifestArtifact[] WingetManifests { get; set; } = Array.Empty<PowerForgeWingetManifestArtifact>();

    public PowerForgeWingetSubmissionPlan? WingetSubmissionPlan { get; set; }

    public PowerForgeWingetSubmissionResult? WingetSubmission { get; set; }
}

/// <summary>
/// Optional output settings for unified release aggregation.
/// </summary>
internal sealed class PowerForgeReleaseOutputsOptions
{
    public string? ManifestJsonPath { get; set; }

    public string? ChecksumsPath { get; set; }

    public PowerForgeReleaseStagingOptions? Staging { get; set; }
}

internal sealed class PowerForgeReleaseStagingOptions
{
    public string? RootPath { get; set; }

    public string ModulesPath { get; set; } = "modules";

    public string PackagesPath { get; set; } = "nuget";

    public string PortablePath { get; set; } = "portable";

    public string InstallerPath { get; set; } = "installer";

    public string StorePath { get; set; } = "store";

    public string ToolsPath { get; set; } = "tools";

    public string MetadataPath { get; set; } = "metadata";

    public string OtherPath { get; set; } = "assets";

    public string? ModulesNameTemplate { get; set; }

    public string? PackagesNameTemplate { get; set; }

    public string? PortableNameTemplate { get; set; }

    public string? InstallerNameTemplate { get; set; }

    public string? StoreNameTemplate { get; set; }

    public string? ToolsNameTemplate { get; set; }

    public string? MetadataNameTemplate { get; set; }

    public string? OtherNameTemplate { get; set; }
}

internal sealed class PowerForgeAppleReleaseOptions
{
    public string? ProjectRoot { get; set; }

    public string? Configuration { get; set; }

    public string? ArchiveRoot { get; set; }

    public string? ExportRoot { get; set; }

    public string? TeamId { get; set; }

    public string XcodeBuildExecutable { get; set; } = "xcodebuild";

    public bool Archive { get; set; } = true;

    public bool Upload { get; set; }

    public bool AllowProvisioningUpdates { get; set; } = true;

    public bool ManageAppVersionAndBuildNumber { get; set; }

    public bool UploadSymbols { get; set; } = true;

    public bool GenerateAppStoreInformation { get; set; } = true;

    public string? SigningStyle { get; set; }

    public PowerForgeAppleReleaseAutomationOptions Automation { get; set; } = new();

    internal string? AppStoreConnectApiKeyPath { get; set; }

    internal string? AppStoreConnectApiKeyId { get; set; }

    internal string? AppStoreConnectApiIssuerId { get; set; }

    public string? ScreenshotConfigPath { get; set; }

    public string[] ScreenshotConfigPaths { get; set; } = Array.Empty<string>();

    public string? MetadataConfigPath { get; set; }

    public string[] MetadataConfigPaths { get; set; } = Array.Empty<string>();

    public string? AppInfoConfigPath { get; set; }

    public string[] AppInfoConfigPaths { get; set; } = Array.Empty<string>();

    public bool PrepareDistribution { get; set; }

    public bool SelectBuildForDistribution { get; set; } = true;

    public bool AllowUnprocessedDistributionBuild { get; set; }

    public bool SyncMetadata { get; set; }

    public bool SyncAppInfo { get; set; }

    public bool SyncScreenshots { get; set; }

    public bool ReplaceScreenshots { get; set; }

    public bool CheckReleaseReadiness { get; set; }

    public bool DistributeTestFlight { get; set; }

    public string[] TestFlightBetaGroupIds { get; set; } = Array.Empty<string>();

    public string[] TestFlightBetaGroupNames { get; set; } = Array.Empty<string>();

    public string[] TestFlightTesterEmails { get; set; } = Array.Empty<string>();

    public bool CreateMissingTestFlightTesters { get; set; } = true;

    public bool AllowUnprocessedTestFlightBuild { get; set; }

    public bool SubmitTestFlightBetaReview { get; set; }

    public bool SubmitForReview { get; set; }

    public bool AllowUnselectedReviewBuild { get; set; }

    public bool AllowUnprocessedReviewBuild { get; set; }

    public bool SkipReviewReadinessCheck { get; set; }

    public bool AllowReviewSubmissionWhenNotReady { get; set; }

    public bool ReleaseApprovedVersion { get; set; }

    public bool AllowNonPendingDeveloperRelease { get; set; }

    public AppleAppConfiguration[] Apps { get; set; } = Array.Empty<AppleAppConfiguration>();
}

internal sealed class PowerForgeAppleReleasePlan
{
    public string ProjectRoot { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Release";

    public PowerForgeAppleReleaseAction Action { get; set; }

    public PowerForgeAppleReleaseAutomationOptions Automation { get; set; } = new();

    public string ReceiptPath { get; set; } = string.Empty;

    public bool Archive { get; set; }

    public bool Upload { get; set; }

    public bool SyncScreenshots { get; set; }

    public string? ScreenshotConfigPath { get; set; }

    public string[] ScreenshotConfigPaths { get; set; } = Array.Empty<string>();

    public string? MetadataConfigPath { get; set; }

    public string[] MetadataConfigPaths { get; set; } = Array.Empty<string>();

    public string? AppInfoConfigPath { get; set; }

    public string[] AppInfoConfigPaths { get; set; } = Array.Empty<string>();

    public bool PrepareDistribution { get; set; }

    public bool SelectBuildForDistribution { get; set; } = true;

    public bool AllowUnprocessedDistributionBuild { get; set; }

    public bool SyncMetadata { get; set; }

    public bool SyncAppInfo { get; set; }

    public bool ReplaceScreenshots { get; set; }

    public bool CheckReleaseReadiness { get; set; }

    public bool DistributeTestFlight { get; set; }

    public string[] TestFlightBetaGroupIds { get; set; } = Array.Empty<string>();

    public string[] TestFlightBetaGroupNames { get; set; } = Array.Empty<string>();

    public string[] TestFlightTesterEmails { get; set; } = Array.Empty<string>();

    public bool CreateMissingTestFlightTesters { get; set; } = true;

    public bool AllowUnprocessedTestFlightBuild { get; set; }

    public bool SubmitTestFlightBetaReview { get; set; }

    public bool SubmitForReview { get; set; }

    public bool AllowUnselectedReviewBuild { get; set; }

    public bool AllowUnprocessedReviewBuild { get; set; }

    public bool SkipReviewReadinessCheck { get; set; }

    public bool AllowReviewSubmissionWhenNotReady { get; set; }

    public bool ReleaseApprovedVersion { get; set; }

    public bool AllowNonPendingDeveloperRelease { get; set; }

    public string XcodeBuildExecutable { get; set; } = "xcodebuild";

    public bool AllowProvisioningUpdates { get; set; } = true;

    public bool ManageAppVersionAndBuildNumber { get; set; }

    public bool UploadSymbols { get; set; } = true;

    public bool GenerateAppStoreInformation { get; set; } = true;

    public string SigningStyle { get; set; } = "automatic";

    public string? AppStoreConnectApiKeyPath { get; set; }

    public string? AppStoreConnectApiKeyId { get; set; }

    public string? AppStoreConnectApiIssuerId { get; set; }

    public PowerForgeAppleAppReleaseTargetPlan[] Apps { get; set; } = Array.Empty<PowerForgeAppleAppReleaseTargetPlan>();
}

internal sealed class PowerForgeAppleAppReleaseTargetPlan
{
    public string Name { get; set; } = string.Empty;

    public string? BundleId { get; set; }

    public ApplePlatform Platform { get; set; }

    public AppleArchiveVariant ArchiveVariant { get; set; }

    public string? AppStoreConnectAppId { get; set; }

    public string ProjectPath { get; set; } = string.Empty;

    public bool IsWorkspace { get; set; }

    public string Scheme { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Release";

    public string Destination { get; set; } = string.Empty;

    public string ArchivePath { get; set; } = string.Empty;

    public string ExportPath { get; set; } = string.Empty;

    public string? TeamId { get; set; }

    public bool Upload { get; set; }

    public bool VersionUpdateRequested { get; set; }

    public string? MarketingVersion { get; set; }

    public string? BuildNumber { get; set; }

    public AppleBuildNumberPolicy BuildNumberPolicy { get; set; } = AppleBuildNumberPolicy.KeepExisting;

    public bool GenerateProjectIfMissing { get; set; }

    public bool RegenerateProject { get; set; }

    public string XcodeGenExecutable { get; set; } = "xcodegen";

    public int ProjectGenerationTimeoutSeconds { get; set; } = 120;
}

internal sealed class PowerForgeAppleAppReleaseResult
{
    public PowerForgeAppleAppReleaseTargetPlan Plan { get; set; } = new();

    public AppleAppArchiveResult? Archive { get; set; }

    public AppleAppArchiveUploadResult? Upload { get; set; }

    public XcodeProjectVersionUpdateResult? VersionUpdate { get; set; }

    public AppStoreConnectReleasePreparationResult? Distribution { get; set; }

    public AppStoreConnectTestFlightDistributionResult? TestFlight { get; set; }

    public AppStoreConnectBetaAppReviewSubmissionResult? TestFlightBetaReviewSubmission { get; set; }

    public AppStoreConnectReviewSubmissionResult? ReviewSubmission { get; set; }

    public AppStoreConnectVersionReleaseResult? VersionRelease { get; set; }

    public AppStoreConnectReleaseStateResult? RemoteState { get; set; }

    public bool ResumedExistingBuild { get; set; }

    public bool ProjectGenerated { get; set; }

    public string[] SkippedSteps { get; set; } = Array.Empty<string>();

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }
}

internal sealed class PowerForgeReleaseGitHubOptions
{
    public bool Publish { get; set; }

    public PowerForgeReleaseVersionSource VersionSource { get; set; } = PowerForgeReleaseVersionSource.Auto;

    public string? Owner { get; set; }

    public string? Repository { get; set; }

    public string? Token { get; set; }

    public string? TokenFilePath { get; set; }

    public string? TokenEnvName { get; set; }

    public bool GenerateReleaseNotes { get; set; } = true;

    public bool IsPreRelease { get; set; }

    public bool ReplaceExistingAssets { get; set; }

    public string? TagTemplate { get; set; }

    public string? ReleaseNameTemplate { get; set; }
}

internal enum PowerForgeReleaseVersionSource
{
    Auto,
    Module,
    Packages,
    Assets
}

internal sealed class PowerForgeReleaseWingetOptions
{
    public bool Enabled { get; set; }

    public string? OutputPath { get; set; }

    public string? InstallerUrlTemplate { get; set; }

    public string ManifestVersion { get; set; } = "1.12.0";

    public string PackageLocale { get; set; } = "en-US";

    public bool Submit { get; set; }

    public PowerForgeReleaseWingetSubmissionOptions Submission { get; set; } = new();

    public PowerForgeReleaseWingetPackage[] Packages { get; set; } = Array.Empty<PowerForgeReleaseWingetPackage>();
}

internal sealed class PowerForgeReleaseWingetSubmissionOptions
{
    public bool? Enabled { get; set; }

    public PowerForgeWingetSubmissionMode Mode { get; set; } = PowerForgeWingetSubmissionMode.Manifest;

    public string ToolPath { get; set; } = "wingetcreate";

    public string? Token { get; set; }

    public string? TokenFilePath { get; set; }

    public string TokenEnvName { get; set; } = "WINGET_CREATE_GITHUB_TOKEN";

    public string? PullRequestTitle { get; set; }

    public bool NoOpen { get; set; } = true;

    public bool Replace { get; set; }

    public string? ReplaceVersion { get; set; }

    public bool AllowInteractiveAuthentication { get; set; }

    public int TimeoutSeconds { get; set; } = 900;
}

internal enum PowerForgeWingetSubmissionMode
{
    Manifest,
    Update
}

internal sealed class PowerForgeWingetManifestArtifact
{
    public string PackageIdentifier { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string[] InstallerUrls { get; set; } = Array.Empty<string>();
}

internal sealed class PowerForgeWingetSubmissionPlan
{
    public bool Enabled { get; set; }

    public PowerForgeWingetSubmissionMode Mode { get; set; }

    public string ToolPath { get; set; } = "wingetcreate";

    public string WorkingDirectory { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 900;

    public bool UsesToken { get; set; }

    public bool UsesInteractiveAuthentication { get; set; }

    public bool NoOpen { get; set; }

    public PowerForgeWingetSubmissionEntryPlan[] Entries { get; set; } = Array.Empty<PowerForgeWingetSubmissionEntryPlan>();
}

internal sealed class PowerForgeWingetSubmissionEntryPlan
{
    public string PackageIdentifier { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string[] InstallerUrls { get; set; } = Array.Empty<string>();

    [JsonIgnore]
    internal string[] Arguments { get; set; } = Array.Empty<string>();

    public string[] RedactedArguments { get; set; } = Array.Empty<string>();
}

internal sealed class PowerForgeWingetSubmissionResult
{
    public bool Succeeded { get; set; }

    public string? ErrorMessage { get; set; }

    public PowerForgeWingetSubmissionEntryResult[] Entries { get; set; } = Array.Empty<PowerForgeWingetSubmissionEntryResult>();
}

internal sealed class PowerForgeWingetSubmissionEntryResult
{
    public string PackageIdentifier { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string[] RedactedArguments { get; set; } = Array.Empty<string>();

    public int ExitCode { get; set; }

    public bool Succeeded { get; set; }

    public bool TimedOut { get; set; }

    public string StdOut { get; set; } = string.Empty;

    public string StdErr { get; set; } = string.Empty;
}

internal sealed class PowerForgeReleaseWingetPackage
{
    public string PackageIdentifier { get; set; } = string.Empty;

    public string? PackageVersion { get; set; }

    public string? PackageLocale { get; set; }

    public string Publisher { get; set; } = string.Empty;

    public string? PublisherUrl { get; set; }

    public string PackageName { get; set; } = string.Empty;

    public string? PackageUrl { get; set; }

    public string License { get; set; } = string.Empty;

    public string? LicenseUrl { get; set; }

    public string ShortDescription { get; set; } = string.Empty;

    public string? Moniker { get; set; }

    public string[] Tags { get; set; } = Array.Empty<string>();

    public string[] Platform { get; set; } = new[] { "Windows.Desktop" };

    public string? MinimumOSVersion { get; set; }

    public string? ManifestVersion { get; set; }

    public PowerForgeReleaseWingetInstaller[] Installers { get; set; } = Array.Empty<PowerForgeReleaseWingetInstaller>();
}

internal sealed class PowerForgeReleaseWingetInstaller
{
    public PowerForgeReleaseAssetCategory Category { get; set; } = PowerForgeReleaseAssetCategory.Portable;

    public string? Target { get; set; }

    public string? Runtime { get; set; }

    public string? Framework { get; set; }

    public string? Architecture { get; set; }

    public string InstallerType { get; set; } = "zip";

    public string? NestedInstallerType { get; set; } = "portable";

    public string? RelativeFilePath { get; set; }

    public string? UrlTemplate { get; set; }
}

internal enum PowerForgeReleaseToolOutputKind
{
    Tool,
    Portable,
    Installer,
    Store
}

internal enum PowerForgeReleaseAssetCategory
{
    Module,
    Package,
    Portable,
    Installer,
    Store,
    Tool,
    Metadata,
    Other
}

internal sealed class PowerForgeModuleReleaseOptions
{
    public string? RepositoryRoot { get; set; }

    public string? ScriptPath { get; set; }

    public string? ModulePath { get; set; }

    public string? ManifestPath { get; set; }

    public bool IncludesPackages { get; set; }

    public string? Framework { get; set; }

    public bool? NoDotnetBuild { get; set; }

    public string? ModuleVersion { get; set; }

    public string? PreReleaseTag { get; set; }

    public bool? NoSign { get; set; }

    public bool? SignModule { get; set; }

    public int TimeoutSeconds { get; set; } = 7200;

    public string[] ArtifactPaths { get; set; } = Array.Empty<string>();
}

internal sealed class PowerForgeModuleReleasePlanSummary
{
    public string RepositoryRoot { get; set; } = string.Empty;

    public string ScriptPath { get; set; } = string.Empty;

    public string ModulePath { get; set; } = string.Empty;

    public string? ManifestPath { get; set; }

    public string? Configuration { get; set; }

    public string? Framework { get; set; }

    public ConfigurationGateMode RunMode { get; set; } = ConfigurationGateMode.Build;

    public bool IncludesPackages { get; set; }

    public bool IncludesProjectPackages { get; set; }

    public int TimeoutSeconds { get; set; }

    public bool NoDotnetBuild { get; set; }

    public string? ModuleVersion { get; set; }

    public string? PreReleaseTag { get; set; }

    public bool NoSign { get; set; }

    public bool SignModule { get; set; }

    public bool PowerForgeReleaseStage { get; set; }

    public bool UnifiedGitHubRelease { get; set; }

    public string[] ArtifactPaths { get; set; } = Array.Empty<string>();
}

internal sealed class PowerForgeReleaseAssetEntry
{
    public string Path { get; set; } = string.Empty;

    public PowerForgeReleaseAssetCategory Category { get; set; }

    public string? Source { get; set; }

    public string? Target { get; set; }

    public string? PackageId { get; set; }

    public string? Version { get; set; }

    public string? Runtime { get; set; }

    public string? Framework { get; set; }

    public string? Style { get; set; }

    public string? BundleId { get; set; }

    public string? RelativeStagePath { get; set; }

    public string? StagedPath { get; set; }
}

internal sealed class PowerForgeWorkspaceValidationOptions
{
    public string? ConfigPath { get; set; }

    public string? Profile { get; set; }

    public string[] EnableFeatures { get; set; } = Array.Empty<string>();

    public string[] DisableFeatures { get; set; } = Array.Empty<string>();
}

/// <summary>
/// GitHub publishing result for one tool release group.
/// </summary>
internal sealed class PowerForgeToolGitHubReleaseResult
{
    public string Owner { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;

    public string[] AssetPaths { get; set; } = Array.Empty<string>();

    public bool Success { get; set; }

    public string? ReleaseUrl { get; set; }

    public bool ReusedExistingRelease { get; set; }

    public string? ErrorMessage { get; set; }

    public string[] SkippedExistingAssets { get; set; } = Array.Empty<string>();

    public string[] ReplacedExistingAssets { get; set; } = Array.Empty<string>();
}

internal sealed class PowerForgeUnifiedGitHubReleaseResult
{
    public string Owner { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;

    public string[] AssetPaths { get; set; } = Array.Empty<string>();

    public bool Success { get; set; }

    public string? ReleaseUrl { get; set; }

    public bool ReusedExistingRelease { get; set; }

    public string? ErrorMessage { get; set; }

    public string[] SkippedExistingAssets { get; set; } = Array.Empty<string>();

    public string[] ReplacedExistingAssets { get; set; } = Array.Empty<string>();
}
