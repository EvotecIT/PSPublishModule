namespace PowerForge;

/// <summary>
/// Optional progress hook for pipeline execution.
/// Hosts (CLI, VSCode extension, etc.) can use this to render a consistent, pre-planned progress UI
/// while keeping the pipeline logic in the core library.
/// </summary>
public interface IModulePipelineProgressReporter
{
    /// <summary>Called when a step starts.</summary>
    void StepStarting(ModulePipelineStep step);

    /// <summary>Called when a step completes successfully.</summary>
    void StepCompleted(ModulePipelineStep step);

    /// <summary>Called when a step fails (before the exception is propagated).</summary>
    void StepFailed(ModulePipelineStep step, Exception error);
}

