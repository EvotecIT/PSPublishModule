using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseBuildHandoffTests
{
    [Fact]
    public async Task HandoffUsesCanonicalBuildCheckpointAndRequiresExistingArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifact = Path.Combine(root, "fixture.nupkg");
        try
        {
            await File.WriteAllTextAsync(artifact, "fixture");
            var service = new ReleaseBuildHandoffService();
            var result = Build(root, artifact);
            var handoff = await service.PrepareAsync(result);
            var item = Assert.Single(handoff.Session.Items);
            Assert.Equal(ReleaseQueueStage.Sign, item.Stage);
            Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, item.Status);
            var checkpoint = new ReleaseBuildCheckpointReader().TryReadBuildResult(item);
            Assert.NotNull(checkpoint); Assert.True(checkpoint.Succeeded); Assert.Equal(root, checkpoint.RootPath);
            Assert.Equal(artifact, Assert.Single(Assert.Single(checkpoint.AdapterResults).ArtifactFiles));
            Assert.Equal(artifact, Assert.Single(handoff.Artifacts).ArtifactPath);
            File.Delete(artifact);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.PrepareAsync(result));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(result with { Succeeded = false }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(result with { AdapterResults = [] }));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(Build(root, "relative.nupkg")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ReleaseBuildExecutionResult Build(string root, string artifact) => new(root, true, "Built", 1,
        [new(ReleaseBuildAdapterKind.ProjectBuild, true, "Built", 0, 1, [], [artifact])]);
}
