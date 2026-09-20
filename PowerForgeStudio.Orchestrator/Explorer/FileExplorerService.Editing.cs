using PowerForge;

namespace PowerForgeStudio.Orchestrator.Explorer;

public sealed partial class FileExplorerService
{
    /// <summary>Opens an editable Unicode document inside a working copy without traversing links or Git metadata.</summary>
    public Task<RepositoryTextDocument> OpenTextDocumentAsync(string workingCopyRoot, string path, CancellationToken cancellationToken = default)
        => Task.Run(() => new RepositoryTextFileEditor().Open(ValidateOperationPath(Path.GetFullPath(workingCopyRoot), path)), cancellationToken);

    /// <summary>Checks working-copy containment and saves through the shared byte-snapshot transaction.</summary>
    public Task<RepositoryTextDocument> SaveTextDocumentAsync(string workingCopyRoot, RepositoryTextDocument original, string text, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(original);
            ValidateOperationPath(Path.GetFullPath(workingCopyRoot), original.Path);
            return new RepositoryTextFileEditor().Save(original, text);
        }, cancellationToken);
}
