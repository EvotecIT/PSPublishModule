namespace PowerForgeStudio.Domain.Hub;

/// <summary>Durable record for an item moved out of a working copy and available for restoration.</summary>
public sealed record WorkspaceFileRecoveryEntry(
    string Id,
    string WorkingCopyRoot,
    string OriginalPath,
    string RecoveryPath,
    bool IsDirectory,
    int ItemCount,
    long SizeBytes,
    DateTimeOffset DeletedAtUtc);
