using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseBuildCheckpointReader
{
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer = new();

    public ReleaseBuildExecutionResult? TryReadBuildResult(ReleaseQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _checkpointSerializer.TryRead<ReleaseBuildExecutionResult>(item, "sign.waiting.usb");
    }

    public IReadOnlyList<ReleaseSigningArtifact> BuildSigningManifest(IEnumerable<ReleaseQueueItem> queueItems)
    {
        var artifacts = new List<ReleaseSigningArtifact>();

        foreach (var item in queueItems.Where(queueItem => queueItem.Stage == ReleaseQueueStage.Sign && queueItem.Status == ReleaseQueueItemStatus.WaitingApproval))
        {
            var buildResult = TryReadBuildResult(item);
            if (buildResult is null)
            {
                continue;
            }

            foreach (var adapterResult in buildResult.AdapterResults)
            {
                foreach (var artifactFile in adapterResult.ArtifactFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    artifacts.Add(new ReleaseSigningArtifact(
                        RepositoryName: item.RepositoryName,
                        AdapterKind: adapterResult.AdapterKind.ToString(),
                        ArtifactPath: artifactFile,
                        ArtifactKind: "File"));
                }

                foreach (var artifactDirectory in adapterResult.ArtifactDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    artifacts.Add(new ReleaseSigningArtifact(
                        RepositoryName: item.RepositoryName,
                        AdapterKind: adapterResult.AdapterKind.ToString(),
                        ArtifactPath: artifactDirectory,
                        ArtifactKind: "Directory"));
                }
            }
        }

        return artifacts
            .DistinctBy(artifact => $"{artifact.AdapterKind}|{artifact.ArtifactKind}|{artifact.ArtifactPath}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(artifact => artifact.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(artifact => artifact.ArtifactPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
