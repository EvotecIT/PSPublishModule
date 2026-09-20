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
    [InlineData("changed")]
    [InlineData("missing-file")]
    [InlineData("missing-digest")]
    [InlineData("failed-signing")]
    [InlineData("requires-rebuild")]
    [InlineData("failed-receipt")]
    [InlineData("wrong-root")]
    [InlineData("wrong-receipt-root")]
    [InlineData("wrong-stage")]
    [InlineData("wrong-status")]
    public async Task StandaloneProjectRejectsInvalidSigningEvidenceBeforeAnyPublisher(string fault)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-publication-checkpoint-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            File.WriteAllText(Path.Combine(build, "project.build.json"), """{"PublishNuget":true,"PublishApiKey":"fixture-key"}""");
            File.WriteAllText(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var package = Path.Combine(root, "Fixture.1.0.0.nupkg");
            File.WriteAllText(package, "approved contents");
            var receipt = ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(root, "Fixture", "ProjectBuild",
                package, "File", ReleaseSigningReceiptStatus.Signed, "Signed", DateTimeOffset.UtcNow));
            if (fault == "changed") File.WriteAllText(package, "changed after signing");
            if (fault == "missing-file") File.Delete(package);
            if (fault == "missing-digest") receipt = receipt with { ContentSha256 = null };
            if (fault == "failed-receipt") receipt = receipt with { Status = ReleaseSigningReceiptStatus.Failed };
            if (fault == "wrong-receipt-root") receipt = receipt with { RootPath = Path.Combine(root, "other") };
            var signing = new ReleaseSigningExecutionResult(fault == "wrong-root" ? Path.Combine(root, "other") : root,
                fault != "failed-signing", "Signing", null, [receipt]) { RequiresRebuild = fault == "requires-rebuild" };
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository,
                1, fault == "wrong-stage" ? ReleaseQueueStage.Sign : ReleaseQueueStage.Publish,
                fault == "wrong-status" ? ReleaseQueueItemStatus.Failed : ReleaseQueueItemStatus.ReadyToRun,
                "Ready", "publish.ready", JsonSerializer.Serialize(signing), DateTimeOffset.UtcNow);
            var calls = 0;
            var service = new ReleasePublishExecutionService(new RepositoryCatalogScanner(), new ModuleBuildHostService(),
                new ProjectBuildHostService(), new ProjectBuildCommandHostService(), new ProjectBuildPublishHostService(),
                (_, _) => { calls++; throw new InvalidOperationException("Publisher must not run."); });
            using var environment = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(item);
            Assert.False(result.Succeeded);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, Assert.Single(result.Receipts).Status);
            Assert.Equal(0, calls);
            if (fault is not ("wrong-stage" or "wrong-status"))
                Assert.Equal("ConfigurationError", Assert.Single(service.BuildPendingTargets([item])).TargetKind);
        }
        finally { Directory.Delete(root, true); }
    }
}
