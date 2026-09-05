using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// One terminal region considered by the canonical Hybrid promotion policy, including the exact
/// reason it was promoted or retained as authored PowerShell.
/// </summary>
public sealed class PowerShellCompilationRegionCandidate
{
    /// <summary>Creates immutable region-candidate decision evidence.</summary>
    [JsonConstructor]
    public PowerShellCompilationRegionCandidate(
        string regionId,
        string sourceSha256,
        string sourceDocumentSha256,
        string sourceName,
        int sourceLine,
        string sourcePath,
        int startOffset,
        int endOffset,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        bool promoted,
        string decisionCode,
        string reason,
        string generatedName,
        PowerShellCompilationRegionGraph? regionGraph)
    {
        RegionId = regionId ?? string.Empty;
        SourceSha256 = sourceSha256 ?? string.Empty;
        SourceDocumentSha256 = sourceDocumentSha256 ?? string.Empty;
        SourceName = sourceName ?? string.Empty;
        SourceLine = Math.Max(0, sourceLine);
        SourcePath = sourcePath ?? string.Empty;
        StartOffset = Math.Max(0, startOffset);
        EndOffset = Math.Max(StartOffset, endOffset);
        StartLine = Math.Max(0, startLine);
        StartColumn = Math.Max(0, startColumn);
        EndLine = Math.Max(StartLine, endLine);
        EndColumn = Math.Max(0, endColumn);
        Promoted = promoted;
        DecisionCode = decisionCode ?? string.Empty;
        Reason = reason ?? string.Empty;
        GeneratedName = generatedName ?? string.Empty;
        RegionGraph = regionGraph;
    }

    /// <summary>Stable authored region identity.</summary>
    public string RegionId { get; }
    /// <summary>SHA-256 of the exact authored candidate text.</summary>
    public string SourceSha256 { get; }
    /// <summary>SHA-256 of the complete authored document that supplied the function ABI.</summary>
    public string SourceDocumentSha256 { get; }
    /// <summary>Name of the retained authored function containing the candidate.</summary>
    public string SourceName { get; }
    /// <summary>One-based start line of the retained function body.</summary>
    public int SourceLine { get; }
    /// <summary>Full authored source path.</summary>
    public string SourcePath { get; }
    /// <summary>Zero-based authored candidate start offset.</summary>
    public int StartOffset { get; }
    /// <summary>Zero-based authored candidate end offset.</summary>
    public int EndOffset { get; }
    /// <summary>One-based authored candidate start line.</summary>
    public int StartLine { get; }
    /// <summary>One-based authored candidate start column.</summary>
    public int StartColumn { get; }
    /// <summary>One-based authored candidate end line.</summary>
    public int EndLine { get; }
    /// <summary>One-based authored candidate end column.</summary>
    public int EndColumn { get; }
    /// <summary>Whether the candidate was emitted and selected for Hybrid execution.</summary>
    public bool Promoted { get; }
    /// <summary>Stable policy decision code suitable for aggregation.</summary>
    public string DecisionCode { get; }
    /// <summary>Human-readable explanation produced by the same promotion policy.</summary>
    public string Reason { get; }
    /// <summary>Generated helper name when promoted; otherwise an empty string.</summary>
    public string GeneratedName { get; }
    /// <summary>Canonical lowered graph when the candidate reached lowering; otherwise null.</summary>
    public PowerShellCompilationRegionGraph? RegionGraph { get; }
}
