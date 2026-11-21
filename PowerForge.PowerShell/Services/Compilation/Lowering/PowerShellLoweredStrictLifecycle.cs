namespace PowerForge;

/// <summary>Explicit clauses and storage for the attached and standalone routes of a compiled stage.</summary>
internal sealed record PowerShellLoweredStrictLifecycle(
    PowerShellSymbolId Attachment,
    PowerShellSymbolId? Limit,
    PowerShellLoweredStatement[] SharedLocals,
    PowerShellLoweredStatement[] DirectBody,
    PowerShellLoweredStatement[] Begin,
    PowerShellLoweredForEachStatement Process,
    PowerShellLoweredStatement[] End,
    PowerShellLoweredStatement[] Cleanup);
