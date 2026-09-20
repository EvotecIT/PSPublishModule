namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleaseBuildExecutionService
{
    /// <summary>Builds the current working-copy contracts with PowerForge publication disabled.
    /// Progress can arrive from worker threads; presentation hosts must marshal updates to their UI.
    /// Project code remains trusted, and local project signing follows its configuration.</summary>
    Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default,
        IProgress<ReleaseBuildProgress>? progress = null);
}
