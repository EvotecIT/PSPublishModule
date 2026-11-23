namespace PowerForge;

/// <summary>
/// Identifies a stable authored source range without exposing parser-specific syntax objects.
/// </summary>
internal readonly struct SourceSpan : IEquatable<SourceSpan>
{
    internal SourceSpan(
        string documentId,
        int startOffset,
        int endOffset,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        if (startOffset < 0) throw new ArgumentOutOfRangeException(nameof(startOffset));
        if (endOffset < startOffset) throw new ArgumentOutOfRangeException(nameof(endOffset));
        DocumentId = documentId ?? string.Empty;
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    internal string DocumentId { get; }
    internal int StartOffset { get; }
    internal int EndOffset { get; }
    internal int StartLine { get; }
    internal int StartColumn { get; }
    internal int EndLine { get; }
    internal int EndColumn { get; }

    public bool Equals(SourceSpan other)
        => StartOffset == other.StartOffset &&
           EndOffset == other.EndOffset &&
           string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(DocumentId) * 397) ^ StartOffset ^ EndOffset;
        }
    }
}
