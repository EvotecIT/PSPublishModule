using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Avalonia.Tests;

[CollectionDefinition("Local project publication", DisableParallelization = true)]
public sealed class LocalProjectPublicationCollection;

[Collection("Local project publication")]
public sealed class LocalProjectReleaseFlowTests
{
    [Fact]
    public async Task DiscoveredJsonProjectPublishesAndVerifiesOnlyTheReviewedLocalFeed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-local-release-" + Guid.NewGuid().ToString("N"))).FullName;
        var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var build = Directory.CreateDirectory(Path.Combine(project, "Build")).FullName;
        var feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        var database = Path.Combine(root, "release-history.db");
        var oldPublish = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_ENABLE_PUBLISH");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(project, "LocalRelease.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version>
                  <PackageId>LocalReleaseFixture</PackageId><Authors>Validation</Authors>
                  <Description>Local Studio release fixture</Description><NuGetAudit>false</NuGetAudit>
                </PropertyGroup></Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(project, "Fixture.cs"), "public sealed class Fixture { public int Value => 42; }");
            await File.WriteAllTextAsync(Path.Combine(project, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(build, "project.build.json"), JsonSerializer.Serialize(new
            {
                RootPath = "..", ExpectedVersion = "1.0.0", OutputPath = "artifacts/packages",
                CreateReleaseZip = false, SignAssemblies = false, SignPackages = false,
                PublishNuget = true, PublishSource = feed, PublishApiKey = "local-feed-only", PublishGitHub = false
            }));
            Environment.SetEnvironmentVariable("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            await TestAppBuilder.RunAsync(async () =>
            {
                using var release = new ReleaseViewModel(signing: new DurableReleaseSigningWorkflow(database, new IntegrityCheckpoint()),
                    history: new ReleaseHistoryService(database), publication: new ReleasePublicationPreviewService(),
                    publishing: new DurableReleasePublicationWorkflow(database), verification: new DurableReleaseVerificationWorkflow(database));
                using var workspace = new WorkspaceViewModel(root, release: release);
                await workspace.RefreshAsync();
                var local = Assert.Single(workspace.Projects, item => item.Path == project);
                await workspace.SelectAsync(local);
                Assert.False(workspace.HasGitWorkingCopy);
                workspace.ShowBuildCommand.Execute(null);
                await workspace.Build.PlanAsync();
                Assert.True(workspace.Build.CanBuild, workspace.Build.Status);
                await workspace.Build.BuildAsync();
                Assert.True(workspace.Build.BuildResult?.Succeeded, workspace.Build.BuildStatus + "\n" + workspace.Build.BuildOutput);
                var package = Assert.Single(workspace.Build.BuildResult!.AdapterResults.SelectMany(x => x.ArtifactFiles),
                    x => x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                Assert.Empty(Directory.GetFiles(feed, "*.nupkg", SearchOption.AllDirectories));

                workspace.ShowReleaseCommand.Execute(null);
                await release.PrepareAsync();
                Assert.True(release.CanSign, release.SigningConfigurationStatus);
                await release.SignAsync();
                Assert.True(release.SigningResult?.Succeeded, release.Status);
                await release.InspectPublicationAsync();
                Assert.Equal(feed, Assert.Single(release.PublicationTargets).Destination);
                Assert.False(release.CanPublish);
                release.ConfirmPublication = true;
                Assert.True(release.CanPublish);
                await release.PublishAsync();
                Assert.True(release.Stage == "Published · ready to verify", release.Stage + ": " + release.Status);
                var feedPackage = Assert.Single(Directory.GetFiles(feed, "*.nupkg", SearchOption.AllDirectories));
                Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(package)), SHA256.HashData(await File.ReadAllBytesAsync(feedPackage)));
                await release.VerifyAsync();
                Assert.True(release.Stage == "Release verified", release.Stage + ": " + release.Status);
                Assert.Single(release.VerificationReceipts);
                Assert.Equal(ReleaseQueueStage.Completed, release.Handoff!.Session.Items.Single().Stage);

                var window = new MainWindow { DataContext = workspace, Width = 1050, Height = 720 };
                window.Show();
                try
                {
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "local-release-verified.png"), PngBitmapEncoderOptions.Default);
                        window.GetVisualDescendants().OfType<ReleaseView>().Single()
                            .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var bottom = window.CaptureRenderedFrame(); Assert.NotNull(bottom);
                        bottom.Save(Path.Combine(output, "local-release-verified-bottom.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", oldPublish);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class IntegrityCheckpoint : IReleaseSigningWorkflow
    {
        public Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default)
        {
            var item = handoff.Session.Items.Single();
            var receipts = handoff.Artifacts.Where(artifact => artifact.ArtifactPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                .Select(artifact => new ReleaseSigningReceipt(
                item.RootPath, item.RepositoryName, artifact.AdapterKind, artifact.ArtifactPath, artifact.ArtifactKind,
                ReleaseSigningReceiptStatus.Signed, "Local package integrity checkpoint; no certificate signature.", DateTimeOffset.UtcNow)
                { ContentSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifact.ArtifactPath))) }).ToArray();
            var execution = new ReleaseSigningExecutionResult(item.RootPath, true, "Local package integrity captured.", item.CheckpointStateJson, receipts);
            return Task.FromResult(new ReleaseSigningWorkflowResult(
                new ReleaseQueueRunner().CompleteSigning(handoff.Session, item.RootPath, execution).Session, execution));
        }
    }
}
