using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ReleasePublicationFlowTests
{
    [Fact]
    public async Task ReviewedTargetsPublishAndVerifyThroughDurableWorkflowSurfaces()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-release-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var preview = new Preview(root);
                var publishing = new Publishing();
                var verification = new Verification();
                using var release = new ReleaseViewModel(new Handoff(root), new Signing(), publication: preview,
                    publishing: publishing, verification: verification);
                release.SetBuild(new(root, true, "Built", 1, []), false, false);
                await release.PrepareAsync();
                await release.SignAsync();
                Assert.True(release.CanInspectPublication);
                Assert.True(release.ShowPublicationDetails);
                Assert.False(release.CanReviewPublication);
                await release.InspectPublicationAsync();
                Assert.True(release.CanReviewPublication);
                Assert.False(release.CanPublish);
                release.ConfirmPublication = true;
                Assert.True(release.CanPublish);

                await release.PublishAsync();

                Assert.Equal(1, publishing.Calls);
                Assert.Single(release.PublicationReceipts);
                Assert.False(release.CanReviewPublication);
                Assert.True(release.CanVerify);
                await release.VerifyAsync();
                Assert.Equal(1, verification.Calls);
                Assert.Single(release.VerificationReceipts);
                Assert.Equal("Release verified", release.Stage);
                Assert.Equal("Publication and verification receipts are recorded below.", release.PublicationSummary);
                Assert.Equal(ReleaseQueueStage.Completed, release.Handoff!.Session.Items.Single().Stage);
                Assert.Contains(release.ExecutionProgress, item => item.Stage == ReleaseQueueStage.Publish && item.State == "Published");
                Assert.Contains(release.ExecutionProgress, item => item.Stage == ReleaseQueueStage.Verify && item.State == "Verified");
                Assert.Equal(1, release.ProgressCompleted);
                Assert.Equal(1, release.ProgressTotal);

                using var workspace = new WorkspaceViewModel(root, release: release);
                workspace.ShowReleaseCommand.Execute(null);
                Assert.False(workspace.ShowGenericProjectContext);
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var context = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border => border.Name == "ContextPanel");
                    Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Release checkpoint" && block.IsEffectivelyVisible);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "release-publish-verify.png"), PngBitmapEncoderOptions.Default);
                        window.GetVisualDescendants().OfType<ReleaseView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var bottom = window.CaptureRenderedFrame(); Assert.NotNull(bottom);
                        bottom.Save(Path.Combine(output, "release-publish-verify-bottom.png"), PngBitmapEncoderOptions.Default);
                        window.Width = 1050; window.Height = 720; window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var compact = window.CaptureRenderedFrame(); Assert.NotNull(compact);
                        compact.Save(Path.Combine(output, "release-publish-verify-compact.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ChangedDestinationInvalidatesApprovalBeforePublisherRuns()
    {
        var root = Path.GetTempPath();
        var preview = new Preview(root) { ChangeOnSecondRead = true };
        var publishing = new Publishing();
        using var release = new ReleaseViewModel(new Handoff(root), new Signing(), publication: preview,
            publishing: publishing, verification: new Verification());
        release.SetBuild(new(root, true, "Built", 1, []), false, false);
        await release.PrepareAsync(); await release.SignAsync(); await release.InspectPublicationAsync();
        release.ConfirmPublication = true;

        await release.PublishAsync();

        Assert.Equal(0, publishing.Calls);
        Assert.False(release.ConfirmPublication);
        Assert.Equal("Destinations changed", release.Stage);
    }

    [Fact]
    public async Task UnsavedPublicationEvidenceBlocksReleaseUntilLocalRetrySucceeds()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var root = Path.GetTempPath();
            var publishing = new PendingPublishing();
            using var release = new ReleaseViewModel(new Handoff(root), new Signing(), publication: new Preview(root),
                publishing: publishing, verification: new Verification());
            using var workspace = new WorkspaceViewModel(root, release: release);
            release.SetBuild(new(root, true, "Built", 1, []), false, false);
            await release.PrepareAsync(); await release.SignAsync(); await release.InspectPublicationAsync();
            release.ConfirmPublication = true;

            await release.PublishAsync();

            Assert.True(release.HasUnpersistedEvidence);
            Assert.True(release.HasProtectedReleaseWork);
            Assert.True(workspace.Build.IsReleaseRunning);
            Assert.False(release.CanVerify);
            Assert.Throws<InvalidOperationException>(() => release.SetBuild(null, true, false));
            workspace.ShowReleaseCommand.Execute(null);
            workspace.ShowFilesCommand.Execute(null);
            Assert.True(workspace.IsReleasePage);
            Assert.Contains("unsaved receipts", release.Status);
            await release.RetrySaveReceiptsAsync();
            Assert.False(release.HasUnpersistedEvidence);
            Assert.False(workspace.Build.IsReleaseRunning);
            Assert.True(release.CanVerify);
            Assert.Equal(1, publishing.PublishCalls);
            Assert.Equal(1, publishing.SaveCalls);
            return true;
        });
    }

    private sealed class Handoff(string root) : IReleaseBuildHandoffService
    {
        public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken token = default)
        {
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            return Task.FromResult(new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), []));
        }
    }

    private sealed class Signing : IReleaseSigningWorkflow
    {
        public Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default)
        {
            var item = handoff.Session.Items.Single();
            var execution = new ReleaseSigningExecutionResult(item.RootPath, true, "Signed", item.CheckpointStateJson, []);
            return Task.FromResult(new ReleaseSigningWorkflowResult(new ReleaseQueueRunner().CompleteSigning(handoff.Session, item.RootPath, execution).Session, execution));
        }
    }

    private sealed class Preview(string root) : IReleasePublicationPreviewService
    {
        private int _calls;
        public bool ChangeOnSecondRead { get; init; }
        public Task<ReleasePublicationPreview> PreviewAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
        {
            var destination = ChangeOnSecondRead && ++_calls > 1 ? "https://feed.example.test/changed" : "https://feed.example.test/v3";
            return Task.FromResult(new ReleasePublicationPreview(
                [new(root, "Fixture", "ProjectBuild", "Fixture.1.0.0.nupkg", "NuGet", Path.Combine(root, "Fixture.1.0.0.nupkg"), destination)],
                "Destination inspection only. Nothing has been published."));
        }
    }

    private sealed class Publishing : IReleasePublicationWorkflow
    {
        public int Calls;
        public Task<ReleasePublicationWorkflowResult> PublishAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
            => PublishAsync(session, cancellationToken, progress: null);

        public async Task<ReleasePublicationWorkflowResult> PublishAsync(
            ReleaseQueueSession session,
            CancellationToken cancellationToken,
            IReleaseArtifactProgressSink? progress)
        {
            Calls++;
            var item = session.Items.Single();
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Publish, "Fixture.1.0.0.nupkg", "Fixture.1.0.0.nupkg", "Publishing", 0, 1,
                    "Publishing fixture package.", DateTimeOffset.UtcNow), CancellationToken.None);
            var receipt = new ReleasePublishReceipt(item.RootPath, item.RepositoryName, "ProjectBuild", "Fixture.1.0.0.nupkg", "NuGet",
                "https://feed.example.test/v3", "Fixture.1.0.0.nupkg", ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Publish, receipt.TargetName, receipt.SourcePath, "Published", 1, 1,
                    receipt.Summary, DateTimeOffset.UtcNow), CancellationToken.None);
            var execution = new ReleasePublishExecutionResult(item.RootPath, true, "Published", item.CheckpointStateJson, [receipt]);
            return new ReleasePublicationWorkflowResult(new ReleaseQueueRunner().CompletePublish(session, item.RootPath, execution).Session, execution);
        }
        public Task<ReleasePublicationWorkflowResult> RetrySaveAsync(ReleasePublicationWorkflowResult result, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class PendingPublishing : IReleasePublicationWorkflow
    {
        public int PublishCalls;
        public int SaveCalls;
        public Task<ReleasePublicationWorkflowResult> PublishAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            var item = session.Items.Single();
            var receipt = new ReleasePublishReceipt(item.RootPath, item.RepositoryName, "ProjectBuild", "Fixture", "NuGet",
                "https://feed.example.test/v3", "Fixture.nupkg", ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
            var execution = new ReleasePublishExecutionResult(item.RootPath, true, "Published", item.CheckpointStateJson, [receipt]);
            var completed = new ReleaseQueueRunner().CompletePublish(session, item.RootPath, execution).Session;
            return Task.FromResult(new ReleasePublicationWorkflowResult(completed, execution)
                { PersistenceError = "Fixture save failure", PendingCheckpoint = session });
        }
        public async Task<ReleasePublicationWorkflowResult> PublishAsync(
            ReleaseQueueSession session,
            CancellationToken cancellationToken,
            IReleaseArtifactProgressSink? progress)
        {
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Publish, "Fixture", "Fixture.nupkg", "Publishing", 0, 1,
                    "Publishing fixture package.", DateTimeOffset.UtcNow), CancellationToken.None);
            var result = await PublishAsync(session, cancellationToken);
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Publish, "Fixture", "Fixture.nupkg", "Published", 1, 1,
                    "Published.", DateTimeOffset.UtcNow), CancellationToken.None);
            return result;
        }
        public Task<ReleasePublicationWorkflowResult> RetrySaveAsync(ReleasePublicationWorkflowResult result, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(result with { PersistenceError = null, PendingCheckpoint = null });
        }
    }

    private sealed class Verification : IReleaseVerificationWorkflow
    {
        public int Calls;
        public Task<ReleaseVerificationWorkflowResult> VerifyAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
            => VerifyAsync(session, cancellationToken, progress: null);

        public async Task<ReleaseVerificationWorkflowResult> VerifyAsync(
            ReleaseQueueSession session,
            CancellationToken cancellationToken,
            IReleaseArtifactProgressSink? progress)
        {
            Calls++;
            var item = session.Items.Single();
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Verify, "Fixture.1.0.0.nupkg", "Fixture.1.0.0.nupkg", "Checking", 0, 1,
                    "Checking fixture feed.", DateTimeOffset.UtcNow), CancellationToken.None);
            var receipt = new ReleaseVerificationReceipt(item.RootPath, item.RepositoryName, "ProjectBuild", "Fixture.1.0.0.nupkg", "NuGet",
                "https://feed.example.test/v3", ReleaseVerificationReceiptStatus.Verified, "Verified", DateTimeOffset.UtcNow);
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Verify, receipt.TargetName, "Fixture.1.0.0.nupkg", "Verified", 1, 1,
                    receipt.Summary, DateTimeOffset.UtcNow), CancellationToken.None);
            var execution = new ReleaseVerificationExecutionResult(item.RootPath, true, "Verified", item.CheckpointStateJson, [receipt]);
            return new ReleaseVerificationWorkflowResult(new ReleaseQueueRunner().CompleteVerification(session, item.RootPath, execution).Session, execution);
        }
        public Task<ReleaseVerificationWorkflowResult> RetrySaveAsync(ReleaseVerificationWorkflowResult result, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
