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

/// <summary>
/// Optional extension for hosts that want to render skipped steps (e.g., when the pipeline aborts early).
/// </summary>
public interface IModulePipelineProgressReporterV2 : IModulePipelineProgressReporter
{
    /// <summary>Called when a step is skipped (not executed).</summary>
    void StepSkipped(ModulePipelineStep step);
}

/// <summary>
/// Optional extension for hosts that can render determinate progress within a running step.
/// </summary>
public interface IModulePipelineProgressReporterV3 : IModulePipelineProgressReporterV2
{
    /// <summary>Updates the current and maximum values for a running step.</summary>
    void StepProgress(ModulePipelineStep step, double value, double maximum, string? detail = null);
}

/// <summary>
/// Optional extension for hosts that render durable work items produced by a nested
/// project/package workflow inside a module pipeline step.
/// </summary>
public interface IModulePipelineProgressReporterV4 : IModulePipelineProgressReporterV3
{
    /// <summary>Registers nested work items in their owning release phase.</summary>
    void ItemsPlanned(
        PowerForgeReleaseProgressPhase phase,
        IReadOnlyList<PowerForgeReleaseProgressItem> items);

    /// <summary>Updates one nested work item.</summary>
    void ItemUpdated(
        PowerForgeReleaseProgressItem item,
        PowerForgeReleaseProgressItemState state,
        string? detail = null);
}
