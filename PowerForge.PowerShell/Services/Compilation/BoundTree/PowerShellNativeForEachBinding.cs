namespace PowerForge;

/// <summary>Preserves the native loop variable and the source positions of collection evaluation and advancement.</summary>
internal sealed record PowerShellNativeForEachBinding(PowerShellNativeAssignmentTarget Target,
    SourceSpan CollectionSpan, string CollectionSourceText, string VariableSourceText);
