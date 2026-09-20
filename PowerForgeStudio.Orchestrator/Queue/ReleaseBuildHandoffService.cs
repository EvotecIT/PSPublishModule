using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>A captured build and its canonical queue checkpoint, ready for explicit signing.</summary>
public sealed record ReleaseBuildHandoff(ReleaseQueueSession Session, IReadOnlyList<ReleaseSigningArtifact> Artifacts);

public interface IReleaseBuildHandoffService
{
    Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken cancellationToken = default);
}

/// <summary>Adapts a completed standalone build to the existing release queue without executing a release stage.</summary>
public sealed class ReleaseBuildHandoffService : IReleaseBuildHandoffService
{
    public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        return Task.Run(() => Prepare(build, cancellationToken), cancellationToken);
    }

    private static ReleaseBuildHandoff Prepare(ReleaseBuildExecutionResult build, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!build.Succeeded || build.AdapterResults.Count == 0 || build.AdapterResults.Any(adapter => !adapter.Succeeded))
            throw new InvalidOperationException("A successful build is required before preparing a release.");
        var root = Path.GetFullPath(build.RootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The build working copy no longer exists.");
        var repository = new RepositoryCatalogScanner().InspectRepository(root);
        var now = DateTimeOffset.UtcNow;
        var item = new ReleaseQueueItem(root, repository.Name, repository.RepositoryKind, repository.WorkspaceKind, 1,
            ReleaseQueueStage.Build, ReleaseQueueItemStatus.ReadyToRun, "Captured build", "build.ready", null, now);
        var session = ReleaseQueueSessionFactory.Create(root, [item], now);
        session = new ReleaseQueueRunner().CompleteBuild(session, root, build with { RootPath = root }).Session;
        var artifacts = new ReleaseBuildCheckpointReader().BuildSigningManifest(session.Items);
        if (artifacts.Count == 0) throw new InvalidOperationException("The build did not capture release artifacts. Build with an artifact-producing configuration first.");
        foreach (var artifact in artifacts)
        {
            token.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(artifact.ArtifactPath))
                throw new InvalidDataException("A captured artifact path is not absolute. Rebuild before preparing this release.");
            var exists = artifact.ArtifactKind == "Directory" ? Directory.Exists(artifact.ArtifactPath) : File.Exists(artifact.ArtifactPath);
            if (!exists) throw new FileNotFoundException("A captured release artifact is missing. Rebuild before preparing this release.", artifact.ArtifactPath);
        }
        token.ThrowIfCancellationRequested();
        return new(session, artifacts);
    }
}
