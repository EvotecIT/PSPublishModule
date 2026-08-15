namespace PowerForge;

/// <summary>
/// Explicit Apple release operation selected for a unified release run.
/// </summary>
public enum PowerForgeAppleReleaseAction
{
    /// <summary>Honor the legacy action flags stored in the release configuration.</summary>
    Configured,

    /// <summary>Read App Store Connect state without changing it.</summary>
    Status,

    /// <summary>Run local topology, capability, artifact, credential, and remote release-state diagnostics without changing Apple state.</summary>
    Doctor,

    /// <summary>Set the requested marketing version and the next available build number in the configured version source.</summary>
    Version,

    /// <summary>Create signed local archives without uploading them.</summary>
    Archive,

    /// <summary>Create and upload signed archives, then optionally wait for processing.</summary>
    Upload,

    /// <summary>Upload existing signed archives without creating new ones.</summary>
    UploadExisting,

    /// <summary>Prepare Distribution versions, metadata, build selection, and readiness.</summary>
    Prepare,

    /// <summary>Validate and sync configured App Store screenshots.</summary>
    Screenshots,

    /// <summary>Assign a processed build to configured TestFlight groups and testers.</summary>
    TestFlight,

    /// <summary>Run the resumable non-review release steps and stop at the first human approval gate.</summary>
    Advance,

    /// <summary>Submit a processed build to TestFlight Beta App Review.</summary>
    SubmitTestFlightReview,

    /// <summary>Submit a ready Distribution version to App Review.</summary>
    SubmitAppReview,

    /// <summary>Release a version that is waiting for developer release.</summary>
    Release,

    /// <summary>Remove release artifacts only from the configured Apple artifact roots.</summary>
    Cleanup,

    /// <summary>Create signed archives and complete local export validation without uploading or notarizing them.</summary>
    Rehearse,

    /// <summary>Execute one reviewed, resumable TestFlight and App Store Review shipping intent.</summary>
    Ship
}

/// <summary>Current durable phase of one Apple Ship intent.</summary>
public enum PowerForgeAppleShipPhase
{
    /// <summary>The checked-in Apple version source must be updated, reviewed, and merged before shipping resumes.</summary>
    VersionCheckpoint,

    /// <summary>The exact merged source can be archived, uploaded, prepared, and submitted according to the target routes.</summary>
    Release
}

/// <summary>
/// Reusable automation policy for Apple release execution.
/// </summary>
internal sealed class PowerForgeAppleReleaseAutomationOptions
{
    /// <summary>Write a compact receipt after explicit Apple actions.</summary>
    public bool WriteReceipt { get; set; } = true;

    /// <summary>Receipt path relative to the Apple project root.</summary>
    public string ReceiptPath { get; set; } = "build/powerforge/apple/release-receipt.json";

    /// <summary>Directory containing immutable Apple release attempt receipts.</summary>
    public string ReceiptHistoryPath { get; set; } = "build/powerforge/apple/receipts";

    /// <summary>Plan receipt path relative to the Apple project root.</summary>
    public string PlanReceiptPath { get; set; } = "build/powerforge/apple/release-plan.json";

    /// <summary>Exclusive operation lock path relative to the Apple project root.</summary>
    public string LockPath { get; set; } = "build/powerforge/apple/release.lock";

    /// <summary>Optional checked-in XcodeGen project.yml used as the authoritative version source.</summary>
    public string? VersionSourcePath { get; set; }

    /// <summary>
    /// Optional PSPublishModule X-pattern that resolves to a two- or three-part
    /// Apple marketing version, such as 1.X or X.0.0.
    /// Version reuses the highest compatible unreleased train and advances the
    /// pattern only when every known compatible train is already occupied by a
    /// non-editable App Store version.
    /// </summary>
    public string? MarketingVersionPattern { get; set; }

    /// <summary>Reuse an exact remote build instead of uploading the same version/build again.</summary>
    public bool Resume { get; set; } = true;

    /// <summary>Wait for an uploaded build to reach a terminal processing state.</summary>
    public bool WaitForProcessing { get; set; } = true;

    /// <summary>Maximum time spent waiting for App Store Connect processing.</summary>
    public int ProcessingTimeoutSeconds { get; set; } = 1800;

    /// <summary>Delay between App Store Connect state checks.</summary>
    public int PollIntervalSeconds { get; set; } = 20;

    /// <summary>Minimum free disk space required before archive creation.</summary>
    public double MinimumFreeSpaceGB { get; set; }

    /// <summary>Remove stale release artifacts before archive creation.</summary>
    public bool CleanupBeforeArchive { get; set; }

    /// <summary>Remove expired local archive/export artifacts after the remote build is valid.</summary>
    public bool CleanupAfterProcessing { get; set; }

    /// <summary>Age threshold used by bounded stale-artifact cleanup.</summary>
    public int ArtifactRetentionDays { get; set; } = 7;
}

/// <summary>Developer ID export and Apple notarization settings for direct macOS distribution.</summary>
internal sealed class PowerForgeAppleDirectDistributionOptions
{
    /// <summary>Export method passed to xcodebuild.</summary>
    public string ExportMethod { get; set; } = "developer-id";

    /// <summary>xcrun executable used for notarytool and stapler.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>ditto executable used to create a notarization zip for .app bundles.</summary>
    public string DittoExecutable { get; set; } = "ditto";

    /// <summary>spctl executable used for final Gatekeeper assessment.</summary>
    public string SpctlExecutable { get; set; } = "spctl";

    /// <summary>Optional notarytool keychain profile. When omitted, App Store Connect API-key credentials are used.</summary>
    public string? KeychainProfile { get; set; }

    /// <summary>Maximum notarization wait in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 1800;

    /// <summary>Staple and validate accepted tickets on supported artifacts.</summary>
    public bool Staple { get; set; } = true;

    /// <summary>Run Gatekeeper assessment after notarization.</summary>
    public bool Assess { get; set; } = true;
}

/// <summary>
/// Compact, resumable receipt for one Apple release run.
/// </summary>
internal sealed class PowerForgeAppleReleaseReceipt
{
    public int SchemaVersion { get; set; } = 6;

    /// <summary>Unique immutable attempt identity.</summary>
    public string? AttemptId { get; set; }

    public PowerForgeAppleReleaseAction Action { get; set; }

    public string? SourceCommit { get; set; }

    public bool PlanOnly { get; set; }

    /// <summary>Durability checkpoint represented by this immutable receipt.</summary>
    public string? OperationPhase { get; set; }

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// SHA-256 binding the approved action. Ship binds a durable exact-source intent that remains stable
    /// while its own attested remote operations progress; other actions also bind observed Apple state.
    /// </summary>
    public string? PlanSha256 { get; set; }

    /// <summary>Canonical SHA-256 of effective mutation flags and every local payload consumed by this plan.</summary>
    public string? MutationInputsSha256 { get; set; }

    /// <summary>Project-relative content hashes for configuration and asset files consumed by this plan.</summary>
    public Dictionary<string, string> MutationInputFiles { get; set; } = new(StringComparer.Ordinal);

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ReceiptPath { get; set; }

    /// <summary>Project-relative immutable history path for this attempt.</summary>
    public string? HistoryPath { get; set; }

    /// <summary>Canonical SHA-256 of the previous immutable attempt receipt.</summary>
    public string? PreviousReceiptSha256 { get; set; }

    /// <summary>Canonical SHA-256 of this receipt with this property omitted.</summary>
    public string? ReceiptSha256 { get; set; }

    /// <summary>Legacy schema-5 machine-local HMAC retained only for reading historical receipts; it is not recovery authority.</summary>
    public string? ReceiptAuthenticationSha256 { get; set; }

    /// <summary>True when the operator explicitly authorized recovery after independently verifying the remote Apple operation.</summary>
    public bool AdoptExistingBuild { get; set; }

    /// <summary>Current phase when Action is Ship.</summary>
    public PowerForgeAppleShipPhase? ShipPhase { get; set; }

    public PowerForgeAppleVersionReceipt? Versioning { get; set; }

    public PowerForgeAppleReleaseTargetReceipt[] Targets { get; set; } = Array.Empty<PowerForgeAppleReleaseTargetReceipt>();

    public PowerForgeAppleReleaseCleanupReceipt Cleanup { get; set; } = new();

    public PowerForgeAppleReleaseDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerForgeAppleReleaseDiagnostic>();

    public string[] NextActions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Version identity selected for an Apple release.
/// </summary>
internal sealed class PowerForgeAppleVersionReceipt
{
    public string? SourcePath { get; set; }

    public string? RequestedMarketingVersion { get; set; }

    public string? MarketingVersionPattern { get; set; }

    public string MarketingVersion { get; set; } = string.Empty;

    public string BuildNumber { get; set; } = string.Empty;

    public string? PreviousMarketingVersion { get; set; }

    public string? PreviousBuildNumber { get; set; }

    public long HighestRemoteBuildNumber { get; set; }

    public string? HighestRemoteMarketingVersion { get; set; }

    public bool ReusedUnreleasedMarketingVersion { get; set; }

    public bool Changed { get; set; }
}

/// <summary>
/// Remote App Store and TestFlight version evidence for one configured target.
/// </summary>
internal sealed class PowerForgeAppleRemoteVersionInventory
{
    public AppStoreConnectVersionInfo[] AppStoreVersions { get; set; } = Array.Empty<AppStoreConnectVersionInfo>();

    public AppStoreConnectBuildInfo[] Builds { get; set; } = Array.Empty<AppStoreConnectBuildInfo>();
}

/// <summary>
/// Compact state for one configured Apple target.
/// </summary>
internal sealed class PowerForgeAppleReleaseTargetReceipt
{
    public string Name { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public string? BundleId { get; set; }

    public ApplePlatform Platform { get; set; }

    public string Configuration { get; set; } = "Release";

    public string? ProjectPath { get; set; }

    public bool IsWorkspace { get; set; }

    public string? Scheme { get; set; }

    public AppleArchiveVariant ArchiveVariant { get; set; }

    public string? Destination { get; set; }

    public AppleDistributionRoute DistributionRoute { get; set; }

    public AppleProductRole ProductRole { get; set; }

    public string? ParentTarget { get; set; }

    public string[] Capabilities { get; set; } = Array.Empty<string>();

    public AppleTestFlightPolicy TestFlightPolicy { get; set; }

    /// <summary>True when this target is included in the Ship internal-TestFlight intent.</summary>
    public bool ShipToTestFlight { get; set; }

    /// <summary>True when this target is included in the Ship App Store Review intent.</summary>
    public bool ShipToAppStoreReview { get; set; }

    public string? AppId { get; set; }

    public bool AppIdDiscovered { get; set; }

    public string? Version { get; set; }

    public string? Build { get; set; }

    public string? BuildId { get; set; }

    public string? BuildProcessingState { get; set; }

    public string? BuildUploadId { get; set; }

    public string? DistributionVersionId { get; set; }

    public string? DistributionState { get; set; }

    public bool? BuildSelected { get; set; }

    public string? TestFlightInternalState { get; set; }

    public string? TestFlightExternalState { get; set; }

    public string? TestFlightReviewState { get; set; }

    public string? AppReviewSubmissionId { get; set; }

    public string? AppReviewState { get; set; }

    public bool ReadinessChecked { get; set; }

    public bool? ReadyForSubmission { get; set; }

    public int? ScreenshotCount { get; set; }

    public string[]? ScreenshotDeliveryStates { get; set; }

    public string? ScreenshotInventorySha256 { get; set; }

    public AppStoreConnectReleaseReadinessCheck[]? ReadinessChecks { get; set; }

    public string? ReadinessSha256 { get; set; }

    public bool TestFlightBetaGroupsConfigured { get; set; }

    public AppStoreConnectControlPlaneState? ControlPlane { get; set; }

    public AppStoreConnectGovernancePlan? Governance { get; set; }

    public bool ArchiveCreated { get; set; }

    public bool ProjectGenerated { get; set; }

    public bool UploadPerformed { get; set; }

    /// <summary>True when a local export completed without an upload or notarization submission.</summary>
    public bool ExportRehearsed { get; set; }

    /// <summary>Project-relative locally exported artifact path produced by Rehearse.</summary>
    public string? RehearsalArtifactPath { get; set; }

    /// <summary>SHA-256 of the locally exported artifact produced by Rehearse.</summary>
    public string? RehearsalArtifactSha256 { get; set; }

    /// <summary>Hash contract used by RehearsalArtifactSha256: file-content or filesystem-identity-v2.</summary>
    public string? RehearsalArtifactSha256Kind { get; set; }

    /// <summary>Project-relative archive path used for an upload attempt.</summary>
    public string? ArchivePath { get; set; }

    /// <summary>SHA-256 of the exact local archive used for an upload attempt.</summary>
    public string? ArchiveSha256 { get; set; }

    /// <summary>Attempt id that originally attested the uploaded archive.</summary>
    public string? UploadAttestationAttemptId { get; set; }

    /// <summary>SHA-256 binding the effective archive, signing, export, and App Store upload controls.</summary>
    public string? UploadExecutionSha256 { get; set; }

    public string? DirectArtifactPath { get; set; }

    public string? DirectArtifactSha256 { get; set; }

    /// <summary>SHA-256 binding the effective archive, export, signing, and notarization controls that produced the direct artifact.</summary>
    public string? DirectExecutionSha256 { get; set; }

    public string? NotarizationSubmissionId { get; set; }

    /// <summary>SHA-256 of the exact file accepted by Apple's notary service.</summary>
    public string? NotarizationSubmissionSha256 { get; set; }

    public string? NotarizationStatus { get; set; }

    public bool? Stapled { get; set; }

    public bool? StapleValidated { get; set; }

    public bool? GatekeeperAccepted { get; set; }

    public bool ResumedAcceptedNotarization { get; set; }

    public bool ResumedExistingBuild { get; set; }

    /// <summary>True when an existing remote build was adopted without a matching local upload attestation.</summary>
    public bool AdoptedExistingBuild { get; set; }

    public string[] SkippedSteps { get; set; } = Array.Empty<string>();

    public PowerForgeAppleReleaseDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerForgeAppleReleaseDiagnostic>();

    public string[] NextActions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Actionable, machine-readable Apple release diagnostic retained in the compact receipt.
/// </summary>
internal sealed class PowerForgeAppleReleaseDiagnostic
{
    public string Severity { get; set; } = "error";

    public string Category { get; set; } = "unknown";

    public string Code { get; set; } = "APPLE_UNKNOWN";

    public string Summary { get; set; } = string.Empty;

    public string? Evidence { get; set; }

    public string Action { get; set; } = string.Empty;

    public bool Retryable { get; set; }
}

/// <summary>
/// Bounded local artifact cleanup summary.
/// </summary>
internal sealed class PowerForgeAppleReleaseCleanupReceipt
{
    public string[] RemovedPaths { get; set; } = Array.Empty<string>();

    public long ReclaimedBytes { get; set; }

    public double? FreeSpaceGB { get; set; }
}
