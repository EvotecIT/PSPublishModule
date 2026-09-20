using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Explorer;

/// <summary>Asynchronous filesystem operations used by workspace hosts.</summary>
public interface IFileExplorerService
{
    Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default);
    Task ExecuteAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default,
        IProgress<FileTransferProgress>? progress = null);
}
