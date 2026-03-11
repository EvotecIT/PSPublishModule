namespace PowerForge;

internal sealed class ModuleBuildPreparedContext
{
    public string ModuleName { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string? BasePathForScaffold { get; set; }
    public bool UseLegacy { get; set; }
    public ModulePipelineSpec PipelineSpec { get; set; } = new();
    public string? JsonOutputPath { get; set; }
}
