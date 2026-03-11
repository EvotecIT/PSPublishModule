namespace PowerForge;

internal sealed class ModuleBuildWorkflowResult
{
    public bool Succeeded { get; set; }
    public bool UsedInteractiveView { get; set; }
    public bool WrotePolicySummary { get; set; }
    public ModulePipelinePlan? Plan { get; set; }
    public ModulePipelineResult? Result { get; set; }
    public ModulePipelineDiagnosticsPolicyException? PolicyFailure { get; set; }
    public Exception? Error { get; set; }
}
