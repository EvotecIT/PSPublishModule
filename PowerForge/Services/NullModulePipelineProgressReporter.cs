namespace PowerForge;

internal sealed class NullModulePipelineProgressReporter : IModulePipelineProgressReporter
{
    internal static readonly NullModulePipelineProgressReporter Instance = new();

    private NullModulePipelineProgressReporter()
    {
    }

    public void StepStarting(ModulePipelineStep step)
    {
    }

    public void StepCompleted(ModulePipelineStep step)
    {
    }

    public void StepFailed(ModulePipelineStep step, Exception error)
    {
    }
}
