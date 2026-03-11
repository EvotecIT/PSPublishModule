namespace PowerForge;

internal sealed class DotNetPublishPreparedContext
{
    public DotNetPublishSpec Spec { get; set; } = new();
    public string SourceLabel { get; set; } = string.Empty;
    public string? JsonOutputPath { get; set; }
    public bool JsonOnly { get; set; }
    public bool PlanOnly { get; set; }
    public bool ValidateOnly { get; set; }
}
