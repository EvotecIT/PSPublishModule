namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Measured state for one primary checkout or linked Git working copy.</summary>
public sealed record WorkspaceStorageEntry(
    string RepositoryName,
    string Path,
    string PrimaryPath,
    string Branch,
    bool IsPrimary,
    bool Exists,
    bool IsLocked,
    long SizeBytes,
    int ItemCount,
    int ChangeCount,
    string LocalState,
    string DefaultBranch,
    string AncestryState,
    string NextCheck,
    bool IsReviewCandidate,
    string? Warning)
{
    public bool IsChanged => ChangeCount > 0;
    public bool IsBroken => !Exists || string.Equals(LocalState, "Broken reference", StringComparison.Ordinal);
    public string DisplayName => IsPrimary ? $"{RepositoryName} / primary" : $"{RepositoryName} / {Branch}";
    public string KindDisplay => IsPrimary ? "Primary checkout" : "Worktree";
    public string SizeDisplay => FormatBytes(SizeBytes);
    public string LocalStateDisplay => ChangeCount > 0 ? $"{LocalState} · {ChangeCount} change(s)" : LocalState;
    public string AncestryDisplay => string.IsNullOrWhiteSpace(DefaultBranch) ? AncestryState : $"{AncestryState} · {DefaultBranch}";

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GiB"
            : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.0} MiB"
            : bytes >= 1024 ? $"{bytes / 1024d:0.#} KiB"
            : $"{bytes} B";
}
