namespace PowerForge;

/// <summary>Immutable authored target metadata for a native variable constraint transition.</summary>
/// <remarks>The RHS is a separate bound expression; the source document supplies extents only.</remarks>
internal sealed record PowerShellNativeAssignmentTarget(string Text, string SourcePath, string SourceDocument,
    SourceSpan Span, int StartOffset, int EndOffset);
