namespace PowerForgeStudio.Domain.Workspace;

/// <summary>A stale Git worktree registration included in a reviewed cleanup operation.</summary>
public sealed record WorkspacePrunableRegistration(
    string Path,
    string Reason,
    string AdministrativeIdentity,
    string AdministrativePath,
    string GitDirTarget)
{
    public string Display => string.IsNullOrWhiteSpace(Reason) ? Path : $"{Path} — {Reason}";
}

/// <summary>Exact repository metadata evidence captured before Studio removes one stale registration.</summary>
public sealed record WorkspaceStoragePruneReview(
    string WorkspaceRoot,
    string PrimaryPath,
    string SelectedPath,
    bool ContainmentPassed,
    bool PrimaryPassed,
    bool AdministrativeRegistryPassed,
    bool SelectedRegistrationPassed,
    bool PrunableSetPassed,
    IReadOnlyList<WorkspacePrunableRegistration> PrunableRegistrations,
    IReadOnlyList<string> PreservedRegistrations,
    IReadOnlyList<string> Findings,
    string Fingerprint,
    string RegistryIdentityFingerprint,
    DateTimeOffset ReviewedAtUtc)
{
    public bool ReadyForConfirmation => ContainmentPassed && PrimaryPassed && AdministrativeRegistryPassed && SelectedRegistrationPassed &&
                                        PrunableSetPassed && PrunableRegistrations.Count > 0 && Findings.Count == 0;
    public string ContainmentDisplay => Display(ContainmentPassed);
    public string PrimaryDisplay => Display(PrimaryPassed);
    public string AdministrativeRegistryDisplay => Display(AdministrativeRegistryPassed);
    public string SelectedRegistrationDisplay => Display(SelectedRegistrationPassed);
    public string PrunableSetDisplay => PrunableSetPassed
        ? $"{PrunableRegistrations.Count} reviewed"
        : "Failed";

    private static string Display(bool passed) => passed ? "Passed" : "Failed";
}
