namespace PowerForge;

/// <summary>Bounded text editing over the repository file transaction owner.</summary>
public sealed class RepositoryTextFileEditor
{
    /// <summary>Maximum source and draft size supported by the editor.</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>Reads UTF-8 or BOM-marked UTF-16/UTF-32 without modifying the file.</summary>
    public RepositoryTextDocument Open(string path)
        => RepositoryTextFileTransactionService.ReadDocument(path, MaximumBytes);

    /// <summary>
    /// Saves a draft only when its original byte snapshot still matches. Encoding, BOM and
    /// existing line endings are retained. If a concurrent pathname replacement races the save,
    /// the displaced bytes are retained in a named backup and an exception reports its path.
    /// </summary>
    public RepositoryTextDocument Save(RepositoryTextDocument original, string text)
    {
        if (original is null) throw new ArgumentNullException(nameof(original));
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (text.Contains('\0')) throw new ArgumentException("Text cannot contain binary null characters.", nameof(text));
        // UTF-32 uses at most four bytes per UTF-16 code unit; cap allocation before encoding.
        if (text.Length > MaximumBytes) throw new ArgumentException("The draft exceeds the editor size limit.", nameof(text));
        new RepositoryTextFileTransactionService().Apply([
            new RepositoryTextFileUpdate(original.Path, original.Text, text, original.ContentHash, original.MaxBytes)
        ]);
        var saved = Open(original.Path);
        if (!string.Equals(saved.Text, text, StringComparison.Ordinal))
            throw new IOException("The file changed again after saving. Your draft is still available in the editor.");
        return saved;
    }
}
