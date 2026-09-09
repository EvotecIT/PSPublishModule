using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>A separately compiled literal script block owned by an emitted function or another block.</summary>
public sealed class PowerShellCompilationScriptBlockRegion
{
    /// <summary>Creates immutable source identity and nested execution evidence.</summary>
    [JsonConstructor]
    public PowerShellCompilationScriptBlockRegion(string generatedMemberName, int startOffset, int endOffset,
        PowerShellCompilationRegionGraph graph)
    {
        GeneratedMemberName = generatedMemberName ?? string.Empty;
        StartOffset = startOffset;
        EndOffset = endOffset;
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>Private generated method containing the block's executable clauses.</summary>
    public string GeneratedMemberName { get; }
    /// <summary>Zero-based authored literal start offset.</summary>
    public int StartOffset { get; }
    /// <summary>Zero-based authored literal end offset.</summary>
    public int EndOffset { get; }
    /// <summary>Regions executed when the block is invoked, rather than when its value is created.</summary>
    public PowerShellCompilationRegionGraph Graph { get; }
}
