namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Read-only workspace storage inventory with logical-size totals.</summary>
public sealed record WorkspaceStorageSnapshot(
    string WorkspaceRoot,
    DateTimeOffset InspectedAtUtc,
    long IndexedBytes,
    long WorktreeBytes,
    int ReviewCandidateCount,
    IReadOnlyList<WorkspaceStorageEntry> Entries,
    IReadOnlyList<WorkspaceStorageOtherFolder>? OtherFolders = null,
    string? OtherFolderScanWarning = null,
    string? RegistrationScanWarning = null)
{
    public bool IsRegistrationInventoryComplete => RegistrationScanWarning is null;
    public string IndexedDisplay => FormatBytes(IndexedBytes);
    public string WorktreeDisplay => FormatBytes(WorktreeBytes);
    public IReadOnlyList<WorkspaceStorageOtherFolder> UnregisteredFolders => OtherFolders ?? [];
    public long OtherFolderBytes => UnregisteredFolders.Where(static item => item.Measured).Sum(static item => item.SizeBytes);
    public int UnmeasuredOtherFolderCount => UnregisteredFolders.Count(static item => !item.Measured);
    public string OtherFolderDisplay => FormatBytes(OtherFolderBytes);

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GiB"
            : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.0} MiB"
            : bytes >= 1024 ? $"{bytes / 1024d:0.#} KiB"
            : $"{bytes} B";
}
