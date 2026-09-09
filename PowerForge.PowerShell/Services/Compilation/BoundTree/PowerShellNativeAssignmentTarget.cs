namespace PowerForge;

/// <summary>Immutable authored target metadata for native storage or receiver mutation.</summary>
/// <remarks>The value or capture body is bound separately; the source document supplies target metadata only.</remarks>
internal sealed record PowerShellNativeAssignmentTarget(string Text, string SourcePath, string SourceDocument,
    SourceSpan Span, int StartOffset, int EndOffset, bool MutatesReceiver = false, string[]? ReadVariables = null);
