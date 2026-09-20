using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceStorageTests
{
    [Fact]
    public async Task StorageRouteRendersMeasuredEvidenceAndFiltersCandidates()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-storage-ui-" + Guid.NewGuid().ToString("N"));
        var root = Directory.CreateDirectory(Path.Combine(fixture, "Workspace")).FullName;
        var repository = Directory.CreateDirectory(Path.Combine(root, "OfficeIMO")).FullName;
        var worktree = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "officeimo-pdf")).FullName;
        var changed = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "officeimo-html")).FullName;
        try
        {
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            var entries = new[]
            {
                Entry("OfficeIMO", repository, repository, "main", isPrimary: true, 240L * 1024 * 1024, "Clean", 0, "Not checked", "Retain primary checkout", false),
                Entry("OfficeIMO", worktree, repository, "pdf-authoring-model", false, 120L * 1024 * 1024, "Clean", 0, "Locally merged", "Refresh remote / PR and active-use evidence", true),
                Entry("OfficeIMO", changed, repository, "html-engine", false, 80L * 1024 * 1024, "Changed", 2, "Not merged", "Preserve or commit local changes", false),
                Entry("PowerForge", Path.Combine(root, "_worktrees", "missing-release"), repository, "release-3.0.143", false, 0, "Broken reference", 0, "Not checked", "Repair or prune the Git worktree reference", false, exists: false)
            };
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root, storage: new FakeStorageInspectionService(entries));
                await model.RefreshAsync();
                await model.ShowStorageCommand.ExecuteAsync(null);
                Assert.True(model.IsStoragePage);
                Assert.Equal(4, model.Storage.Entries.Count);
                Assert.Equal(1, model.Storage.CandidateCount);
                model.Storage.ShowCandidatesCommand.Execute(null);
                Assert.True(Assert.Single(model.Storage.Entries).IsReviewCandidate);
                model.Storage.ShowAllCommand.Execute(null);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "storage-review.png");
                    window.Width = 1050;
                    window.Height = 720;
                    Capture(window, "storage-review-compact.png");
                }
                finally
                {
                    window.Close();
                }
                return true;
            });
        }
        finally
        {
            if (Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
        }
    }

    private static WorkspaceStorageEntry Entry(
        string repository,
        string path,
        string primary,
        string branch,
        bool isPrimary,
        long bytes,
        string state,
        int changes,
        string ancestry,
        string next,
        bool candidate,
        bool exists = true)
        => new(repository, path, primary, branch, isPrimary, exists, false, bytes, 12, changes, state,
            "main", ancestry, next, candidate, null);

    private static void Capture(MainWindow window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, fileName), PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakeStorageInspectionService(IReadOnlyList<WorkspaceStorageEntry> entries) : IWorkspaceStorageInspectionService
    {
        public Task<WorkspaceStorageSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new WorkspaceStorageSnapshot(
                Path.GetFullPath(workspaceRoot),
                DateTimeOffset.UtcNow,
                entries.Sum(static entry => entry.SizeBytes),
                entries.Where(static entry => !entry.IsPrimary).Sum(static entry => entry.SizeBytes),
                entries.Count(static entry => entry.IsReviewCandidate),
                entries));
        }
    }
}
