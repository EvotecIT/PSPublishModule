#if NET8_0_OR_GREATER
namespace PowerForge;

/// <summary>Trusted machine-local policy, never supplied by a GitHub request.</summary>
internal sealed class AppleTestFlightWorkerPolicy
{
    public string Repository { get; set; } = "";
    /// <summary>The enrolled repository's default branch; verified against GitHub before mutation.</summary>
    public string DefaultBranch { get; set; } = "main";
    public string ConsumerRoot { get; set; } = "";
    public string OwnerRoot { get; set; } = "";
    public string OwnerCommit { get; set; } = "";
    public string StateRoot { get; set; } = "";
    public string ScratchRoot { get; set; } = "";
    public string WorkflowPath { get; set; } = ".github/workflows/testflight.yml";
    public string ConfigPath { get; set; } = "powerforge.release.json";
    public DateTimeOffset AcceptRequestsAfter { get; set; }
    public string BaselineSourceCommit { get; set; } = "";
    public string[] RequiredChecks { get; set; } = Array.Empty<string>();
    public string QualificationWorkflowPath { get; set; } = ".github/workflows/apple-ci.yml";
    public AppleTestFlightWorkerTarget[] Targets { get; set; } = Array.Empty<AppleTestFlightWorkerTarget>();
    public string[] PublisherPaths { get; set; } = Array.Empty<string>();
    public string BetaGroupId { get; set; } = "";
    public string BetaGroupName { get; set; } = "Home";
    /// <summary>The enrolled group's audience. Existing internal-only policies retain their behavior.</summary>
    public bool BetaGroupIsInternal { get; set; } = true;
    /// <summary>Standing local authorization to submit the enrolled exact builds to Beta App Review. Public review remains manual.</summary>
    public bool SubmitBetaReviewAutomatically { get; set; }
    /// <summary>Optional PR label that holds releases containing that merged PR until the label is removed.</summary>
    public string ReleaseHoldLabel { get; set; } = "";
}

internal sealed class AppleTestFlightWorkerTarget
{
    public string Name { get; set; } = "";
    public string BundleId { get; set; } = "";
    public string AppId { get; set; } = "";
    public ApplePlatform Platform { get; set; }
}

internal enum AppleTestFlightAutomaticMode { Off, SelectedPRs, EveryMergedPR }

internal enum AppleTestFlightJobStage { Queued, WaitingForChecks, Planning, CheckpointExecuting, CheckpointReady, PublishingCheckpoint, ReleaseReady, Releasing, AppleProcessingPending, ReadyInHome, NeedsOperator, Excluded, ReadyInTestFlight, SubmittingBetaReview, DistributingTestFlight }

internal sealed class AppleTestFlightWorkerState
{
    public int SchemaVersion { get; set; } = 1;
    public string Repository { get; set; } = "";
    public AppleTestFlightAutomaticMode Mode { get; set; }
    public DateTimeOffset ModeChangedAt { get; set; }
    public long ModeChangedByRunId { get; set; }
    public DateTimeOffset HeartbeatAt { get; set; }
    public bool Paused { get; set; }
    public long LastRequestRun { get; set; }
    public long LastScannedRequestRun { get; set; }
    public long RejectedRequestScanCursor { get; set; }
    public List<long> PendingRequestRuns { get; set; } = new();
    public List<AppleTestFlightRequestRecord> Requests { get; set; } = new();
    public List<AppleTestFlightJob> Jobs { get; set; } = new();
}

internal sealed class AppleTestFlightRequestRecord
{
    public string Key { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Url { get; set; } = "";
    public string Operation { get; set; } = "";
    public int PullRequest { get; set; }
    public int Attempt { get; set; }
    public string? SourceCommit { get; set; }
    public bool StatusPending { get; set; }
    public string? Diagnostic { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

internal sealed class AppleTestFlightJob
{
    public string Id { get; set; } = "";
    public bool Automatic { get; set; }
    public AppleTestFlightJobStage Stage { get; set; }
    public List<string> RequestKeys { get; set; } = new();
    public List<int> IncludedPullRequests { get; set; } = new();
    public string? OriginalSource { get; set; }
    public string? Source { get; set; }
    public string? CheckpointCommit { get; set; }
    public string? PlanSha256 { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
    public string? Diagnostic { get; set; }
    public bool StatusPending { get; set; }
    public Dictionary<string, string> BuildIds { get; set; } = new(StringComparer.Ordinal);
    public string? BetaPlanSha256 { get; set; }
    public string[] BetaActionTargets { get; set; } = Array.Empty<string>();
    public DateTimeOffset? BetaActionStartedAt { get; set; }
    public PowerForgeAppleReleaseTargetReceipt[] Outcomes { get; set; } = Array.Empty<PowerForgeAppleReleaseTargetReceipt>();
    public DateTimeOffset UpdatedAt { get; set; }
}
#endif
