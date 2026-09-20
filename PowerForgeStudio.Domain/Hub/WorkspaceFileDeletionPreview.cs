namespace PowerForgeStudio.Domain.Hub;

/// <summary>Reviewed filesystem state that must still match before an item can move to recovery.</summary>
public sealed record WorkspaceFileDeletionPreview(
    string WorkingCopyRoot,
    string SourcePath,
    bool IsDirectory,
    int ItemCount,
    long SizeBytes,
    string SnapshotSha256);
