namespace PowerForge;

/// <summary>
/// Progress callback interface for dotnet publish pipelines.
/// </summary>
public interface IDotNetPublishProgressReporter
{
    /// <summary>Called when a step starts.</summary>
    void StepStarting(DotNetPublishStep step);

    /// <summary>Called when a step completes successfully.</summary>
    void StepCompleted(DotNetPublishStep step);

    /// <summary>Called when a step fails.</summary>
    void StepFailed(DotNetPublishStep step, Exception error);
}

/// <summary>Optional sub-operation detail for hosts that render work inside a planned step.</summary>
internal interface IDotNetPublishProgressReporterV2 : IDotNetPublishProgressReporter
{
    void StepProgress(DotNetPublishStep step, string kind, string detail);
}

