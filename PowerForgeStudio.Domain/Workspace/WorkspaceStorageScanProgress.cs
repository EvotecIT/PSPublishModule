namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Bounded read-only progress while measuring one workspace.</summary>
public sealed record WorkspaceStorageScanProgress(
    int CompletedRepositories,
    int TotalRepositories,
    string WorkingCopyPath,
    long MeasuredBytes,
    int MeasuredItems)
{
    public string MeasuredDisplay => MeasuredBytes >= 1024L * 1024 * 1024
        ? $"{MeasuredBytes / (1024d * 1024 * 1024):0.0} GiB"
        : MeasuredBytes >= 1024L * 1024
            ? $"{MeasuredBytes / (1024d * 1024):0.0} MiB"
            : MeasuredBytes >= 1024
                ? $"{MeasuredBytes / 1024d:0.#} KiB"
                : $"{MeasuredBytes} B";
}
