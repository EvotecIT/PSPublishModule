using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Theory]
    [InlineData("valid", "Single")]
    [InlineData("unreceipted", "Single")]
    [InlineData("unreceipted", "PerProject")]
    [InlineData("mutated-during-plan", "Single")]
    [InlineData("missing-after-plan", "Single")]
    public async Task ExecuteAsync_ProjectGitHubPublish_UsesOnlyCheckpointedPlanAssets(string scenario, string mode)
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# build");

        var zipPath = Path.Combine(repositoryRoot, "Artifacts", "ProjectBuild", "PSPublishModule.1.2.3.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllText(zipPath, "zip");

        File.WriteAllText(
            Path.Combine(buildDirectory, "project.build.json"),
            """
            {
              "PublishGitHub": true,
              // the shared host config reader should resolve this from the environment
              "GitHubAccessTokenEnvName": "PFGH_TOKEN",
              "GitHubUsername": "EvotecIT",
              "GitHubRepositoryName": "PSPublishModule",
              "GitHubGenerateReleaseNotes": true,
            }
            """);

        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Signing completed.",
            SourceCheckpointStateJson: null,
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: zipPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Asset signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        signingResult = signingResult with { Receipts = signingResult.Receipts.Select(ReleaseSigningArtifactIntegrity.Capture).ToArray() };

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var configurationPath = Path.Combine(buildDirectory, "project.build.json");
        File.WriteAllText(configurationPath, File.ReadAllText(configurationPath).Replace("\"PublishGitHub\": true,", "\"PublishGitHub\": true, \"GitHubReleaseMode\": \"" + mode + "\","));
        var otherZip = Path.Combine(repositoryRoot, "Unapproved.zip");
        File.WriteAllText(otherZip, "not signed");
        ProjectBuildGitHubPublishRequest? captured = null;
        var projectBuildHostService = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec => {
                if (scenario == "mutated-during-plan") File.WriteAllText(zipPath, "changed during plan");
                if (scenario == "missing-after-plan") File.Delete(zipPath);
                return new DotNetRepositoryReleaseResult {
                    Success = true,
                    Projects = {
                        new DotNetRepositoryProjectResult {
                            ProjectName = "PSPublishModule",
                            IsPackable = true,
                            NewVersion = "1.2.3",
                            ReleaseZipPath = scenario == "unreceipted" ? otherZip : zipPath
                        }
                    }
                };
            },
            publishGitHub: null,
            validateGitHubPreflight: null);
        var projectBuildPublishHostService = new ProjectBuildPublishHostService(
            new NullLogger(),
            request => {
                captured = request;
                return new ProjectBuildGitHubPublishSummary {
                    Success = true,
                    SummaryTag = "v1.2.3",
                    SummaryReleaseUrl = "https://github.com/EvotecIT/PSPublishModule/releases/tag/v1.2.3",
                    SummaryAssetsCount = 1
                };
            });
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            projectBuildHostService,
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            projectBuildPublishHostService,
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)));

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set("PFGH_TOKEN", "token");

            var result = await service.ExecuteAsync(queueItem);

            if (scenario != "valid")
            {
                Assert.False(result.Succeeded);
                Assert.Null(captured);
                Assert.Equal(ReleasePublishReceiptStatus.Failed, Assert.Single(result.Receipts).Status);
                return;
            }
            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetName}:{receipt.Status}:{receipt.Summary}"))}");
            Assert.NotNull(captured);
            Assert.Equal("EvotecIT", captured!.Owner);
            Assert.Equal("PSPublishModule", captured.Repository);
            Assert.Equal("token", captured.Token);
            Assert.True(captured.GenerateReleaseNotes);
            Assert.Equal("Single", captured.ReleaseMode);
            Assert.Equal(
                zipPath,
                Assert.Single(captured.Release.Projects.Select(project => project.ReleaseZipPath), path => !string.IsNullOrWhiteSpace(path)));
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Contains("GitHub release", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

}
