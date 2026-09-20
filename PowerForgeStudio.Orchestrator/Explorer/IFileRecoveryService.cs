using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Explorer;

/// <summary>Review-first deletion and durable restore operations for workspace files.</summary>
public interface IFileRecoveryService
{
    Task<WorkspaceFileDeletionPreview> InspectDeleteAsync(
        string workingCopyRoot,
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<WorkspaceFileRecoveryEntry> DeleteAsync(
        WorkspaceFileDeletionPreview preview,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceFileRecoveryEntry>> ListAsync(
        string? workingCopyRoot = null,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default);

    Task DeletePermanentlyAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default);
}
