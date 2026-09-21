namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Exact evidence captured before a linked worktree can be removed.</summary>
public sealed record WorkspaceStorageRemovalReview(
    string WorkspaceRoot,
    string PrimaryPath,
    string WorktreePath,
    string HeadSha,
    string RemoteDefaultRef,
    string RemoteDefaultSha,
    bool ContainmentPassed,
    bool RegistrationPassed,
    bool UnlockedPassed,
    bool CleanPassed,
    bool RemoteAncestryPassed,
    int? MergedPullRequestNumber,
    string? MergedPullRequestUrl,
    bool MergedPullRequestPassed,
    bool StudioUsePassed,
    WorkspaceExternalUseEvidence ExternalUseEvidence,
    IReadOnlyList<string> RetainedArtifactPaths,
    IReadOnlyList<string> Findings,
    string Fingerprint,
    DateTimeOffset ReviewedAtUtc)
{
    public bool HasRetainedArtifacts => RetainedArtifactPaths.Count > 0;
    public bool MergeEvidencePassed => RemoteAncestryPassed || MergedPullRequestPassed;
    public bool ExternalUsePassed => !ExternalUseEvidence.HasDetectedProcesses;
    public bool ExternalUseScanPassed => ExternalUseEvidence.IsAvailable && !ExternalUseEvidence.HasDetectedProcesses;
    public bool HasExternalUseWarning => !string.IsNullOrWhiteSpace(ExternalUseEvidence.Warning);
    public bool ReadyForConfirmation => ContainmentPassed && RegistrationPassed && UnlockedPassed && CleanPassed &&
                                        MergeEvidencePassed && StudioUsePassed && ExternalUsePassed && Findings.Count == 0;
    public string ContainmentDisplay => Display(ContainmentPassed);
    public string RegistrationDisplay => Display(RegistrationPassed);
    public string UnlockedDisplay => Display(UnlockedPassed);
    public string CleanDisplay => Display(CleanPassed);
    public bool RemoteAncestrySatisfied => MergeEvidencePassed;
    public bool MergedPullRequestSatisfied => MergeEvidencePassed;
    public string RemoteAncestryDisplay => RemoteAncestryPassed
        ? "Passed"
        : MergedPullRequestPassed ? "Not required; exact PR matched" : "Failed";
    public string MergedPullRequestDisplay => MergedPullRequestPassed
        ? $"PR #{MergedPullRequestNumber} matched exact HEAD"
        : RemoteAncestryPassed ? "Not needed; remote ancestry passed" : "No exact merged PR found";
    public string StudioUseDisplay => Display(StudioUsePassed);
    public string ExternalUseDisplay => ExternalUseEvidence.Display;

    private static string Display(bool passed) => passed ? "Passed" : "Failed";
}
