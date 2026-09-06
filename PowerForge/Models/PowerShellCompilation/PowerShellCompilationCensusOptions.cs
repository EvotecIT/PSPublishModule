namespace PowerForge;

/// <summary>Artifact and semantic contracts used for a source coverage census.</summary>
public sealed class PowerShellCompilationCensusOptions
{
    /// <summary>Requested CLR target framework; null selects the standard net8.0 artifact target.</summary>
    public string? TargetFramework { get; set; }
    /// <summary>Artifact to assess; null infers the kind from each input.</summary>
    public PowerShellCompilationArtifactKind? ArtifactKind { get; set; }
    /// <summary>Compilation mode whose capabilities and shaping are assessed.</summary>
    public PowerShellCompilationMode Mode { get; set; } = PowerShellCompilationMode.Hybrid;
    /// <summary>Named PowerShell semantic profile; empty selects the framework default.</summary>
    public string SemanticProfileId { get; set; } = string.Empty;
    /// <summary>Resolve each input's source closure and evaluate artifact shaping.</summary>
    public bool Recurse { get; set; } = true;
}

/// <summary>An input that could not be resolved or assessed; other inputs remain available.</summary>
public sealed class PowerShellCompilationCensusInputFailure
{
    /// <summary>Requested input path, retained even when it cannot be normalized.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Exception type describing the failed discovery or assessment boundary.</summary>
    public string ErrorType { get; set; } = string.Empty;
    /// <summary>Actionable discovery or assessment diagnostic.</summary>
    public string Message { get; set; } = string.Empty;
}
