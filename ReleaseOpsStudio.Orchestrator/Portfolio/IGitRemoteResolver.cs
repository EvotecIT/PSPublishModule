namespace ReleaseOpsStudio.Orchestrator.Portfolio;

internal interface IGitRemoteResolver
{
    Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default);
}
