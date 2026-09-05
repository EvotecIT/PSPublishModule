using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// One terminal typed region promoted from a function whose authored PowerShell source remains the
/// command surface. The region is not counted as a fully emitted function or binary cmdlet.
/// </summary>
public sealed class PowerShellCompiledRegion
{
    /// <summary>Creates immutable promoted-region evidence.</summary>
    [JsonConstructor]
    public PowerShellCompiledRegion(
        string regionId,
        string sourceSha256,
        string sourceDocumentSha256,
        string sourceName,
        int sourceLine,
        string sourcePath,
        string generatedName,
        string returnType,
        IReadOnlyList<PowerShellCompilationParameter>? inputParameters,
        int startOffset,
        int endOffset,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        IReadOnlyList<PowerShellCompilationSourceMapEntry>? sourceMap,
        PowerShellCompilationRegionGraph regionGraph,
        string documentId)
    {
        RegionId = regionId ?? string.Empty;
        SourceSha256 = sourceSha256 ?? string.Empty;
        SourceDocumentSha256 = sourceDocumentSha256 ?? string.Empty;
        SourceName = sourceName ?? string.Empty;
        SourceLine = Math.Max(0, sourceLine);
        SourcePath = sourcePath ?? string.Empty;
        GeneratedName = generatedName ?? string.Empty;
        ReturnType = returnType ?? string.Empty;
        InputParameters = Array.AsReadOnly((inputParameters ?? Array.Empty<PowerShellCompilationParameter>()).ToArray());
        StartOffset = Math.Max(0, startOffset);
        EndOffset = Math.Max(StartOffset, endOffset);
        StartLine = Math.Max(0, startLine);
        StartColumn = Math.Max(0, startColumn);
        EndLine = Math.Max(StartLine, endLine);
        EndColumn = Math.Max(0, endColumn);
        SourceMap = Array.AsReadOnly((sourceMap ?? Array.Empty<PowerShellCompilationSourceMapEntry>()).ToArray());
        RegionGraph = regionGraph ?? new PowerShellCompilationRegionGraph(Array.Empty<PowerShellCompilationRegion>());
        DocumentId = documentId ?? string.Empty;
    }

    /// <summary>Stable authored region identity.</summary>
    public string RegionId { get; }
    /// <summary>SHA-256 of the exact authored region text selected by the bound front end.</summary>
    public string SourceSha256 { get; }
    /// <summary>SHA-256 of the complete authored document from which the helper ABI was selected.</summary>
    public string SourceDocumentSha256 { get; }
    /// <summary>Name of the retained authored function containing this region.</summary>
    public string SourceName { get; }
    /// <summary>One-based start line of the retained function body.</summary>
    public int SourceLine { get; }
    /// <summary>Full authored source path.</summary>
    public string SourcePath { get; }
    /// <summary>Generated CLR helper member name.</summary>
    public string GeneratedName { get; }
    /// <summary>Stable scalar CLR return type.</summary>
    public string ReturnType { get; }
    /// <summary>Current retained-function parameter values transferred into the region.</summary>
    public IReadOnlyList<PowerShellCompilationParameter> InputParameters { get; }
    /// <summary>Zero-based authored region start offset.</summary>
    public int StartOffset { get; }
    /// <summary>Zero-based authored region end offset.</summary>
    public int EndOffset { get; }
    /// <summary>One-based authored region start line.</summary>
    public int StartLine { get; }
    /// <summary>One-based authored region start column.</summary>
    public int StartColumn { get; }
    /// <summary>One-based authored region end line.</summary>
    public int EndLine { get; }
    /// <summary>One-based authored region end column.</summary>
    public int EndColumn { get; }
    /// <summary>Authored-to-generated mapping for the emitted helper.</summary>
    public IReadOnlyList<PowerShellCompilationSourceMapEntry> SourceMap { get; }
    /// <summary>Canonical bound/lowered execution, transfer, stream, error, and ordering evidence.</summary>
    public PowerShellCompilationRegionGraph RegionGraph { get; }
    /// <summary>Relocation-safe authored document identity.</summary>
    public string DocumentId { get; }

    [JsonIgnore]
    internal string GeneratedSource { get; set; } = string.Empty;
}
