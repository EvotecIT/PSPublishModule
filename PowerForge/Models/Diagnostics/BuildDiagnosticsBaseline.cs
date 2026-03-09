#pragma warning disable CS1591
namespace PowerForge;

public enum BuildDiagnosticBaselineState
{
    Unspecified,
    Existing,
    New
}

public sealed class ModulePipelineDiagnosticsOptions
{
    public string? BaselinePath { get; set; }
    public bool GenerateBaseline { get; set; }
    public bool UpdateBaseline { get; set; }
}

public sealed class BuildDiagnosticsBaselineComparison
{
    public string BaselinePath { get; set; } = string.Empty;
    public bool BaselineLoaded { get; set; }
    public bool BaselineGenerated { get; set; }
    public bool BaselineUpdated { get; set; }
    public int BaselineDiagnosticCount { get; set; }
    public int CurrentDiagnosticCount { get; set; }
    public int ExistingDiagnosticCount { get; set; }
    public int NewDiagnosticCount { get; set; }
    public int ResolvedDiagnosticCount { get; set; }
}
#pragma warning restore CS1591
