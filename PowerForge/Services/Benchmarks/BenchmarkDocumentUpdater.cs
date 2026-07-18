namespace PowerForge;

/// <summary>
/// Updates marker-delimited benchmark blocks inside Markdown documents.
/// </summary>
public sealed class BenchmarkDocumentUpdater
{
    private readonly ManagedMarkdownDocumentUpdater _updater = new();

    /// <summary>
    /// Replaces a benchmark block in a document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Block identifier.</param>
    /// <param name="markdown">Replacement Markdown.</param>
    /// <returns>Update result.</returns>
    public BenchmarkDocumentUpdateResult UpdateBlock(string path, string blockId, string markdown)
    {
        var result = _updater.Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = blockId,
            Markdown = markdown,
            CreateIfMissing = false,
            MissingBlockBehavior = ManagedMarkdownMissingBlockBehavior.Fail
        });

        return new BenchmarkDocumentUpdateResult
        {
            Path = result.Path,
            BlockId = result.BlockId,
            Changed = result.Changed
        };
    }

    /// <summary>
    /// Validates that a benchmark block exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Block identifier.</param>
    public void ValidateBlock(string path, string blockId)
        => _updater.ValidateBlock(path, blockId);
}
