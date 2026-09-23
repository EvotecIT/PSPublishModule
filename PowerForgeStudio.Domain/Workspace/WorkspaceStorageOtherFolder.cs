namespace PowerForgeStudio.Domain.Workspace;

/// <summary>A directory in the workspace's _worktrees container that was not found in the scanned Git registrations.</summary>
public sealed record WorkspaceStorageOtherFolder(
    string Path,
    string Kind,
    long SizeBytes,
    int ItemCount,
    string? Warning,
    bool Measured = true,
    string? GitMetadataState = null)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
    public bool HasGitMetadataState => GitMetadataState is not null;
    public string SizeDisplay => !Measured ? "Not measured"
        : SizeBytes >= 1024L * 1024 * 1024 ? $"{SizeBytes / (1024d * 1024 * 1024):0.0} GiB"
        : SizeBytes >= 1024L * 1024 ? $"{SizeBytes / (1024d * 1024):0.0} MiB"
        : SizeBytes >= 1024 ? $"{SizeBytes / 1024d:0.#} KiB"
        : $"{SizeBytes} B";
}
