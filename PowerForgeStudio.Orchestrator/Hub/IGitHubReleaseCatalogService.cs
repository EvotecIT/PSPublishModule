using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Bounded, read-only GitHub release catalog for a selected working copy.</summary>
public interface IGitHubReleaseCatalogService
{
    Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default);
    Task<GitHubPage<GitHubReleaseMetric>> FetchRecentReleasesAsync(string slug, CancellationToken cancellationToken = default);
}
