using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public interface IProjectHistoryService
{
    Task<ProjectHistoryContext> GetHistoryContextAsync(string repositoryRoot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitLogEntry>> GetHistoryLogAsync(string repositoryRoot, int count = 15, CancellationToken cancellationToken = default);
    Task<GitCommitDetail> GetCommitDetailAsync(string repositoryRoot, string commitHash, CancellationToken cancellationToken = default);
}
