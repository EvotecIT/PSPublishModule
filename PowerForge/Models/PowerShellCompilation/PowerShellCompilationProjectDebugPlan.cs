namespace PowerForge;

/// <summary>Verified artifact and authored-source documents for a debugger session.</summary>
public sealed class PowerShellCompilationProjectDebugPlan
{
    /// <summary>Project-local target selected for debugging.</summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Executable or binary module target kind.</summary>
    public string ArtifactKind { get; set; } = string.Empty;

    /// <summary>Exact artifact path authenticated by the project build receipt.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Portable PDB path authenticated by the project build receipt.</summary>
    public string PdbPath { get; set; } = string.Empty;

    /// <summary>Project working directory for an executable launch or module host.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Verified PDB document paths to current authored PowerShell source files.</summary>
    public Dictionary<string, string> SourceFileMap { get; set; } = new(StringComparer.Ordinal);
}
