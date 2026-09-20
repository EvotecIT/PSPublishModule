using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.Views;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ReleaseHistoryTests
{
    [Fact]
    public async Task FailedSaveProtectsEvidenceAndRetryPersistsWithoutSigningAgain()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-history-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Build"));
            await File.WriteAllTextAsync(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), """{"RootPath":"..","PublishNuget":true,"PublishSource":"https://packages.example.test/v3/index.json"}""");
            var path = Path.Combine(root, "history.db"); var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path, "CREATE TRIGGER reject_receipt BEFORE INSERT ON release_signing_receipt BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
            var signer = new Signer();
            var durable = new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(signer));
            var history = new ReleaseHistoryService(path);
            await TestAppBuilder.RunAsync(async () =>
            {
                var release = new ReleaseViewModel(new Handoff(root), durable, history);
                using var workspace = new WorkspaceViewModel(root, release: release);
                release.SetBuild(new(root, true, "Built", 1, []), false, false); await release.PrepareAsync();
                release.ConfirmDiscardReceipts = true;
                await release.SignAsync();
                Assert.False(release.ConfirmDiscardReceipts);
                release.DiscardUnsavedReceiptsCommand.Execute(null);
                Assert.True(release.HasUnpersistedEvidence);
                Assert.True(release.HasUnpersistedEvidence); Assert.Single(release.Receipts);
                Assert.True(workspace.Build.IsReleaseRunning); Assert.False(release.CanBrowseHistory);
                Assert.Throws<InvalidOperationException>(() => release.SetBuild(null, true, false));
                workspace.ShowReleaseCommand.Execute(null);
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 }; window.Show();
                try
                {
                    window.Close(); Assert.True(window.IsVisible); Capture(window, "release-save-failed.png");
                    window.Width = 1050; window.Height = 720;
                    Capture(window, "release-save-failed-compact.png");
                    window.GetVisualDescendants().OfType<ReleaseView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(window, "release-save-failed-compact-scrolled.png");
                    window.Width = 1600; window.Height = 1000;
                    await new DBAClientX.SQLite().ExecuteNonQueryAsync(path, "DROP TRIGGER reject_receipt;");
                    await release.RetrySaveReceiptsAsync();
                    Assert.False(release.HasUnpersistedEvidence); Assert.False(workspace.Build.IsReleaseRunning);
                    Assert.Equal(1, signer.Calls); Assert.Single((await database.LoadReleaseCheckpointAsync(release.Handoff!.Session.SessionId))!.SigningReceipts);
                    Assert.True(release.CanInspectPublication); await release.InspectPublicationAsync();
                    Assert.Equal("https://packages.example.test/v3/index.json", Assert.Single(release.PublicationTargets).Destination);
                    window.GetVisualDescendants().OfType<ReleaseView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(window, "release-publication-preview.png");
                    window.Width = 1050; window.Height = 720;
                    Capture(window, "release-publication-preview-compact.png");
                    window.Width = 1600; window.Height = 1000;
                    using var reopened = new ReleaseViewModel(history: history);
                    await reopened.RefreshHistoryAsync(); reopened.SelectedHistory = Assert.Single(reopened.History);
                    await reopened.OpenHistoryAsync(); Assert.Single(reopened.Receipts); Assert.False(reopened.CanSign);
                    Assert.False(reopened.CanPrepare); Assert.Equal(root, reopened.BuildRoot);
                    using var reopenedWorkspace = new WorkspaceViewModel(root, release: reopened);
                    reopenedWorkspace.ShowReleaseCommand.Execute(null); window.DataContext = reopenedWorkspace;
                    Capture(window, "release-history.png"); window.Width = 1050; window.Height = 720;
                    Capture(window, "release-history-compact.png");
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        if (name.StartsWith("release-publication-preview", StringComparison.Ordinal))
        {
            window.GetVisualDescendants().OfType<ReleaseView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
            window.UpdateLayout();
        }
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
    }

    private sealed class Handoff(string root) : IReleaseBuildHandoffService
    {
        public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken token = default)
        {
            var item = new ReleaseQueueItem(root, "StudioFixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            return Task.FromResult(new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow),
                [new("StudioFixture", "Project", Path.Combine(root, "fixture.dll"), "File")]));
        }
    }
    private sealed class Signer : IReleaseSigningExecutionService
    {
        public int Calls;
        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token = default)
        {
            Calls++;
            var package = Path.Combine(item.RootPath, "fixture.nupkg");
            File.WriteAllText(package, "signed package fixture");
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));
            return Task.FromResult(new ReleaseSigningExecutionResult(item.RootPath, true, "Signed fixture", item.CheckpointStateJson,
                [new(item.RootPath, item.RepositoryName, "ProjectBuild", package, "File", ReleaseSigningReceiptStatus.Signed, "Signed fixture", DateTimeOffset.UtcNow) { ContentSha256 = hash }]));
        }
    }
}
