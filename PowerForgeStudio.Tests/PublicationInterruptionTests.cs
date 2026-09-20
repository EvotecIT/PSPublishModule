using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Theory]
    [InlineData("cancel-between")]
    [InlineData("cancel-during")]
    [InlineData("cancel-returned-failure")]
    [InlineData("exception")]
    [InlineData("returned-failure")]
    [InlineData("last-success")]
    public async Task PublicationRetainsCompletedReceiptsAndDoesNotReplayUncertainWork(string scenario)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-publish-interrupted-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            File.WriteAllText(Path.Combine(build, "project.build.json"), """{"PublishNuget":true,"PublishApiKey":"fixture-key"}""");
            File.WriteAllText(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var evidence = new List<ReleaseSigningReceipt>();
            for (var i = 0; i < (scenario == "last-success" ? 1 : 3); i++)
            {
                var package = Path.Combine(root, $"Fixture{i}.1.0.0.nupkg");
                File.WriteAllText(package, "approved");
                evidence.Add(ReleaseSigningArtifactIntegrity.Capture(new(root, "Fixture", "ProjectBuild", package,
                    "File", ReleaseSigningReceiptStatus.Signed, "Signed", DateTimeOffset.UtcNow)));
            }
            var signing = new ReleaseSigningExecutionResult(root, true, "Signed", null, evidence);
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository,
                1, ReleaseQueueStage.Publish, ReleaseQueueItemStatus.ReadyToRun, "Ready", "publish.ready", JsonSerializer.Serialize(signing), DateTimeOffset.UtcNow);
            var calls = 0;
            using var cancellation = new CancellationTokenSource();
            var service = new ReleasePublishExecutionService(new RepositoryCatalogScanner(), new ModuleBuildHostService(),
                new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ProjectBuildPublishHostService(),
                (_, token) => {
                    calls++;
                    if (calls == 1 && scenario is "cancel-between" or "last-success") cancellation.Cancel();
                    if (calls == 2 && scenario == "cancel-during") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                    if (calls == 2 && scenario == "cancel-returned-failure")
                    {
                        cancellation.Cancel();
                        return Task.FromResult(new DotNetNuGetPushResult(130, "", "cancelled", "dotnet", TimeSpan.Zero, false, "Cancelled"));
                    }
                    if (calls == 2 && scenario == "exception") throw new IOException("fixture-key must never appear in the result");
                    if (calls == 2 && scenario == "returned-failure")
                        return Task.FromResult(new DotNetNuGetPushResult(1, "", "Rejected", "dotnet", TimeSpan.Zero, false, "Rejected"));
                    return Task.FromResult(new DotNetNuGetPushResult(0, "published", "", "dotnet", TimeSpan.Zero, false, null));
                });
            using var environment = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(item, cancellation.Token);
            var published = Assert.Single(result.Receipts, x => x.Status == ReleasePublishReceiptStatus.Published);
            Assert.Equal(evidence[0].ArtifactPath, published.SourcePath);
            Assert.Equal(scenario is "last-success" or "cancel-between" ? 1 : 2, calls);
            Assert.DoesNotContain("fixture-key", JsonSerializer.Serialize(result));
            if (scenario == "last-success") { Assert.True(result.Succeeded); Assert.False(result.RequiresReconciliation); return; }
            Assert.False(result.Succeeded);
            Assert.True(result.RequiresReconciliation);
            Assert.Equal(scenario is not ("exception" or "returned-failure"), result.WasCancelled);
            Assert.Single(result.Receipts, x => x.Status == ReleasePublishReceiptStatus.Failed);
            var runner = new ReleaseQueueRunner();
            var session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow);
            var failed = runner.FailPublish(session, root, result).Session;
            Assert.False(runner.RetryFailedItem(failed).Changed);
            Assert.False(runner.RetryFailedItems(failed, _ => true).Changed);
        }
        finally { Directory.Delete(root, true); }
    }
}
