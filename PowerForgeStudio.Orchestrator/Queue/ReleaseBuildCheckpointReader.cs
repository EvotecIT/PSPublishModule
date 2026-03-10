using System.Text.Json;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseBuildCheckpointReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public ReleaseBuildExecutionResult? TryReadBuildResult(ReleaseQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.Equals(item.CheckpointKey, "sign.waiting.usb", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(item.CheckpointStateJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleaseBuildExecutionResult>(item.CheckpointStateJson, SerializerOptions);
        }
        catch
        {
            return null;
        }
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
                        AdapterKind: adapterResult.AdapterKind,
                        ArtifactPath: artifactFile,
                        ArtifactKind: "File"));
                }

                if (adapterResult.ArtifactFiles.Count == 0)
                {
                    foreach (var artifactDirectory in adapterResult.ArtifactDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        artifacts.Add(new ReleaseSigningArtifact(
                            RepositoryName: item.RepositoryName,
                            AdapterKind: adapterResult.AdapterKind,
                            ArtifactPath: artifactDirectory,
                            ArtifactKind: "Directory"));
                    }
                }
            }
        }

        return artifacts
            .OrderBy(artifact => artifact.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(artifact => artifact.ArtifactPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
