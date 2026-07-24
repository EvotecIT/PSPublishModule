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

    /// <summary>Submit a processed build to TestFlight Beta App Review.</summary>
    SubmitTestFlightReview,

    /// <summary>Submit a ready Distribution version to App Review.</summary>
    SubmitAppReview,

    /// <summary>Release a version that is waiting for developer release.</summary>
    Release,

    /// <summary>Remove release artifacts only from the configured Apple artifact roots.</summary>
    Cleanup
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

    /// <summary>Remove the exact local archive/export after the remote build is valid.</summary>
    public bool CleanupAfterProcessing { get; set; }

    /// <summary>Age threshold used by bounded stale-artifact cleanup.</summary>
    public int ArtifactRetentionDays { get; set; } = 7;
}

/// <summary>
/// Compact, resumable receipt for one Apple release run.
/// </summary>
internal sealed class PowerForgeAppleReleaseReceipt
{
    public int SchemaVersion { get; set; } = 1;

    public PowerForgeAppleReleaseAction Action { get; set; }

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ReceiptPath { get; set; }

    public PowerForgeAppleReleaseTargetReceipt[] Targets { get; set; } = Array.Empty<PowerForgeAppleReleaseTargetReceipt>();

    public PowerForgeAppleReleaseCleanupReceipt Cleanup { get; set; } = new();

    public string[] NextActions { get; set; } = Array.Empty<string>();
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

    public string? AppId { get; set; }

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

    public bool TestFlightBetaGroupsConfigured { get; set; }

    public bool ArchiveCreated { get; set; }

    public bool ProjectGenerated { get; set; }

    public bool UploadPerformed { get; set; }

    public bool ResumedExistingBuild { get; set; }

    public string[] SkippedSteps { get; set; } = Array.Empty<string>();

    public string[] NextActions { get; set; } = Array.Empty<string>();
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
