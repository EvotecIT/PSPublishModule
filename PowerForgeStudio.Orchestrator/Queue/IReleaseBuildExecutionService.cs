namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleaseBuildExecutionService
{
    Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default);
}
