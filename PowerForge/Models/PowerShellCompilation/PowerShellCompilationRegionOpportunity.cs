using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Continuation shape observed after one analysis-only typed region.</summary>
public enum PowerShellCompilationRegionContinuation
{
    /// <summary>The region reaches the authored end of the function.</summary>
    FunctionEnd,
    /// <summary>The region terminates control flow before any following statement.</summary>
    Terminating,
    /// <summary>The region can continue into a completely bound following statement.</summary>
    BoundFallThrough,
    /// <summary>The region can continue across at least one statement the binder did not represent.</summary>
    UnboundFallThrough
}

/// <summary>One typed value that would cross a prospective retained/CLR region boundary.</summary>
public sealed class PowerShellCompilationRegionTransfer
{
    /// <summary>Creates an immutable prospective boundary-value record.</summary>
    [JsonConstructor]
    public PowerShellCompilationRegionTransfer(
        string identity,
        string typeName,
        string typeProvenance,
        bool stableScalar)
    {
        Identity = identity ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        TypeProvenance = typeProvenance ?? string.Empty;
        StableScalar = stableScalar;
    }

    /// <summary>Canonical parameter, local, or pipeline-variable identity.</summary>
    public string Identity { get; }
    /// <summary>Current canonical CLR type fact.</summary>
    public string TypeName { get; }
    /// <summary>Origin of the CLR type fact, such as Explicit, Inferred, or Unknown.</summary>
    public string TypeProvenance { get; }
    /// <summary>Whether the current bounded terminal ABI already permits this transfer type.</summary>
    public bool StableScalar { get; }
}

/// <summary>
/// One maximal statement-aligned typed region found inside an otherwise rejected function.
/// This record is analysis evidence only and never grants backend emission authority.
/// </summary>
public sealed class PowerShellCompilationRegionOpportunity
{
    /// <summary>Creates immutable analysis-only region-opportunity evidence.</summary>
    [JsonConstructor]
    public PowerShellCompilationRegionOpportunity(
        string opportunityId,
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
        int startStatementIndex,
        int endStatementIndex,
        int statementCount,
        PowerShellCompilationRegionContinuation continuation,
        bool continuationAnalysisComplete,
        bool liveInputSourceAnalysisComplete,
        bool liveOutputConsumerAnalysisComplete,
        bool insideTerminalCandidate,
        IReadOnlyList<PowerShellCompilationRegionTransfer>? liveInputs,
        IReadOnlyList<PowerShellCompilationRegionTransfer>? liveOutputs,
        IReadOnlyList<string>? localCalls,
        PowerShellCompilationRegionGraph regionGraph)
    {
        OpportunityId = opportunityId ?? string.Empty;
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
        StartStatementIndex = Math.Max(0, startStatementIndex);
        EndStatementIndex = Math.Max(StartStatementIndex, endStatementIndex);
        StatementCount = Math.Max(1, statementCount);
        Continuation = continuation;
        ContinuationAnalysisComplete = continuationAnalysisComplete;
        LiveInputSourceAnalysisComplete = liveInputSourceAnalysisComplete;
        LiveOutputConsumerAnalysisComplete = liveOutputConsumerAnalysisComplete;
        InsideTerminalCandidate = insideTerminalCandidate;
        LiveInputs = Array.AsReadOnly((liveInputs ?? Array.Empty<PowerShellCompilationRegionTransfer>()).ToArray());
        LiveOutputs = Array.AsReadOnly((liveOutputs ?? Array.Empty<PowerShellCompilationRegionTransfer>()).ToArray());
        LocalCalls = Array.AsReadOnly((localCalls ?? Array.Empty<string>()).ToArray());
        RegionGraph = regionGraph ?? throw new ArgumentNullException(nameof(regionGraph));
    }

    /// <summary>Opportunity evidence schema version.</summary>
    public int SchemaVersion => 1;
    /// <summary>Stable identity derived from the authored document and exact typed span.</summary>
    public string OpportunityId { get; }
    /// <summary>SHA-256 of the exact authored opportunity text.</summary>
    public string SourceSha256 { get; }
    /// <summary>SHA-256 of the complete authored document.</summary>
    public string SourceDocumentSha256 { get; }
    /// <summary>Name of the retained function containing the opportunity.</summary>
    public string SourceName { get; }
    /// <summary>One-based start line of the retained function body.</summary>
    public int SourceLine { get; }
    /// <summary>Full authored source path.</summary>
    public string SourcePath { get; }
    /// <summary>Zero-based authored opportunity start offset.</summary>
    public int StartOffset { get; }
    /// <summary>Zero-based authored opportunity end offset.</summary>
    public int EndOffset { get; }
    /// <summary>One-based authored opportunity start line.</summary>
    public int StartLine { get; }
    /// <summary>One-based authored opportunity start column.</summary>
    public int StartColumn { get; }
    /// <summary>One-based authored opportunity end line.</summary>
    public int EndLine { get; }
    /// <summary>One-based authored opportunity end column.</summary>
    public int EndColumn { get; }
    /// <summary>Zero-based index of the first top-level authored statement represented.</summary>
    public int StartStatementIndex { get; }
    /// <summary>Zero-based index of the last top-level authored statement represented.</summary>
    public int EndStatementIndex { get; }
    /// <summary>Top-level authored statements represented by this typed region.</summary>
    public int StatementCount { get; }
    /// <summary>Observed control-flow relationship with the retained continuation.</summary>
    public PowerShellCompilationRegionContinuation Continuation { get; }
    /// <summary>Whether every potentially reached following statement was represented by the binder.</summary>
    public bool ContinuationAnalysisComplete { get; }
    /// <summary>Whether every authored predecessor that could establish a live input was represented by the binder.</summary>
    public bool LiveInputSourceAnalysisComplete { get; }
    /// <summary>Whether every potentially reached authored consumer of a live output was represented by the binder.</summary>
    public bool LiveOutputConsumerAnalysisComplete { get; }
    /// <summary>Whether the opportunity falls inside a terminal candidate already considered for execution.</summary>
    public bool InsideTerminalCandidate { get; }
    /// <summary>Typed values read before being written inside the opportunity.</summary>
    public IReadOnlyList<PowerShellCompilationRegionTransfer> LiveInputs { get; }
    /// <summary>
    /// Typed values written by the opportunity and observed by later bound code. When later code is
    /// unbound, this conservatively includes every mutation and consumer completeness is false.
    /// </summary>
    public IReadOnlyList<PowerShellCompilationRegionTransfer> LiveOutputs { get; }
    /// <summary>Canonical local-function identities referenced inside the opportunity.</summary>
    public IReadOnlyList<string> LocalCalls { get; }
    /// <summary>Canonical one-region lowered graph. Its execution route is always Typed.</summary>
    public PowerShellCompilationRegionGraph RegionGraph { get; }
    /// <summary>This evidence cannot authorize backend emission.</summary>
    public bool AnalysisOnly => true;
}
