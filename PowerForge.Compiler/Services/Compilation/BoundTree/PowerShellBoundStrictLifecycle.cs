namespace PowerForge;

/// <summary>Identifies the compiler-owned lifecycle loop, cleanup and direct-call output storage.</summary>
internal sealed record PowerShellBoundStrictLifecycle(
    PowerShellSymbolId Attachment,
    PowerShellSymbolId? Limit,
    PowerShellSymbolId? Collector,
    PowerShellSymbolId Record,
    SourceSpan ProcessSpan,
    bool HasCleanup);
