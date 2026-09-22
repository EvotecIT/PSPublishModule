using System.Security.Cryptography;
using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public async Task ProjectNuGetPublish_PushesRealPackageToDisposableFeedAndRestoresConsumer()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-local-feed-" + Guid.NewGuid().ToString("N"));
        var packageId = "StudioFeedFixture" + Guid.NewGuid().ToString("N")[..8];
        var projectRoot = Path.Combine(root, "project");
        var buildRoot = Path.Combine(root, "Build");
        var outputRoot = Path.Combine(root, "output");
        var feedRoot = Path.Combine(root, "feed");
        var cacheRoot = Path.Combine(root, "packages");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(buildRoot);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(feedRoot);
        try
        {
            var projectPath = Path.Combine(projectRoot, "Fixture.csproj");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version>
                  <PackageId>{packageId}</PackageId><Authors>Validation</Authors>
                  <Description>Disposable Studio feed validation</Description><NuGetAudit>false</NuGetAudit>
                </PropertyGroup></Project>
                """);
            File.WriteAllText(Path.Combine(projectRoot, "Fixture.cs"), "public sealed class Fixture { public int Value => 42; }");
            var nugetConfig = Path.Combine(root, "NuGet.Config");
            File.WriteAllText(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
            File.WriteAllText(Path.Combine(buildRoot, "Build-Project.ps1"), "# Project configuration is inspected without running this script.");
            var configPath = Path.Combine(buildRoot, "project.build.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(new {
                PublishNuget = true,
                PublishSource = feedRoot,
                PublishApiKey = "local-feed-only",
                PublishGitHub = false
            }));

            var runner = new ProcessRunner();
            var restore = await runner.RunAsync(new ProcessRunRequest("dotnet", root,
                ["restore", projectPath, "--configfile", nugetConfig, "--packages", cacheRoot], TimeSpan.FromMinutes(2)));
            Assert.True(restore.Succeeded, restore.StdOut + Environment.NewLine + restore.StdErr);
            var pack = await runner.RunAsync(new ProcessRunRequest("dotnet", root,
                ["pack", projectPath, "-c", "Release", "--no-restore", "-o", outputRoot], TimeSpan.FromMinutes(2)));
            Assert.True(pack.Succeeded, pack.StdOut + Environment.NewLine + pack.StdErr);
            var packagePath = Assert.Single(Directory.GetFiles(outputRoot, "*.nupkg"));

            var signing = new ReleaseSigningExecutionResult(root, true, "Artifact captured for local-feed publication.",
                CreateProjectBuildCheckpoint(root, configPath), [
                    ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(root, packageId,
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(), packagePath, "NuGetPackage",
                        ReleaseSigningReceiptStatus.Signed, "Captured package fixture.", DateTimeOffset.UtcNow))
                ]);
            var item = new ReleaseQueueItem(root, packageId, ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, 1, ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun, "Ready for local-feed publication.", "publish.ready",
                JsonSerializer.Serialize(signing), DateTimeOffset.UtcNow);
            var preview = await new ReleasePublicationPreviewService().PreviewAsync(
                ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow));
            var reviewedTarget = Assert.Single(preview.Targets);
            Assert.Equal("NuGet", reviewedTarget.TargetKind);
            Assert.Equal(feedRoot, reviewedTarget.Destination);

            using var environment = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var published = await new ReleasePublishExecutionService().ExecuteAsync(item);
            Assert.True(published.Succeeded, published.Summary + " " +
                string.Join(" | ", published.Receipts.Select(receipt => receipt.Summary)));
            var receipt = Assert.Single(published.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Equal(packageId, receipt.PackageId);
            Assert.Equal("1.0.0", receipt.PackageVersion);
            var feedPackage = Assert.Single(Directory.GetFiles(feedRoot, "*.nupkg", SearchOption.AllDirectories));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(feedPackage))));
            File.Delete(packagePath);

            var verifyItem = item with {
                Stage = ReleaseQueueStage.Verify,
                Status = ReleaseQueueItemStatus.ReadyToRun,
                CheckpointKey = "verify.ready",
                CheckpointStateJson = JsonSerializer.Serialize(published)
            };
            using var verificationService = new ReleaseVerificationExecutionService();
            var verified = await verificationService.ExecuteAsync(verifyItem);
            Assert.True(verified.Succeeded, verified.Summary + " " +
                string.Join(" | ", verified.Receipts.Select(result => result.Summary)));
            Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(verified.Receipts).Status);

            var consumerRoot = Path.Combine(root, "consumer");
            Directory.CreateDirectory(consumerRoot);
            var consumerPath = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(consumerPath, $"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>
                </PropertyGroup><ItemGroup><PackageReference Include="{packageId}" Version="1.0.0" /></ItemGroup></Project>
                """);
            var consumerRestore = await runner.RunAsync(new ProcessRunRequest("dotnet", root,
                ["restore", consumerPath, "--source", feedRoot, "--packages", cacheRoot], TimeSpan.FromMinutes(2)));
            Assert.True(consumerRestore.Succeeded, consumerRestore.StdOut + Environment.NewLine + consumerRestore.StdErr);
            Assert.True(Directory.Exists(Path.Combine(cacheRoot, packageId.ToLowerInvariant(), "1.0.0")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
