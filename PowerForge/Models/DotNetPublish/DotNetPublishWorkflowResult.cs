namespace PowerForge;

internal sealed class DotNetPublishWorkflowResult
{
    public string? JsonOutputPath { get; set; }
    public DotNetPublishPlan? Plan { get; set; }
    public DotNetPublishResult? Result { get; set; }
}
