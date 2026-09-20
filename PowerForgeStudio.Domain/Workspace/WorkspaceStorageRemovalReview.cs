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
    bool StudioUsePassed,
    IReadOnlyList<string> RetainedArtifactPaths,
    IReadOnlyList<string> Findings,
    string Fingerprint,
    DateTimeOffset ReviewedAtUtc)
{
    public bool HasRetainedArtifacts => RetainedArtifactPaths.Count > 0;
    public bool ReadyForConfirmation => ContainmentPassed && RegistrationPassed && UnlockedPassed && CleanPassed &&
                                        RemoteAncestryPassed && StudioUsePassed && Findings.Count == 0;
    public string ContainmentDisplay => Display(ContainmentPassed);
    public string RegistrationDisplay => Display(RegistrationPassed);
    public string UnlockedDisplay => Display(UnlockedPassed);
    public string CleanDisplay => Display(CleanPassed);
    public string RemoteAncestryDisplay => Display(RemoteAncestryPassed);
    public string StudioUseDisplay => Display(StudioUsePassed);

    private static string Display(bool passed) => passed ? "Passed" : "Failed";
}
