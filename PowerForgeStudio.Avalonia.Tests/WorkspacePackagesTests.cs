using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Globalization;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Packages;
using PowerForgeStudio.Orchestrator.Packages;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspacePackagesTests
{
    [Fact]
    public async Task PackagesRouteFiltersAndPreservesEvidenceWhenRefreshFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-packages-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var source = new FakePackageCatalog();
                using var model = new WorkspaceViewModel(root, packages: source);
                await model.ShowPackagesCommand.ExecuteAsync(null);
                Assert.True(model.IsPackagesPage);
                Assert.True(model.IsWorkspaceUtilityPage);
                Assert.False(model.IsProjectRoute);
                Assert.Equal(2, model.Packages.TotalCount);
                Assert.True(model.Packages.HasWarnings);
                Assert.Contains("PowerShell Gallery", model.Packages.WarningSummary, StringComparison.Ordinal);
                Assert.Equal(1500, int.Parse(model.Packages.TotalDownloads, NumberStyles.AllowThousands, CultureInfo.CurrentCulture));

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    model.Packages.SelectedEntry = model.Packages.Entries[0];
                    Capture(window, "packages-wide.png");
                    model.Packages.ShowNuGetCommand.Execute(null);
                    Assert.Single(model.Packages.Entries);
                    model.Packages.Search = "missing";
                    Assert.Empty(model.Packages.Entries);
                    model.Packages.Search = "Office";
                    Assert.Single(model.Packages.Entries);
                    model.Packages.ShowAllCommand.Execute(null);
                    model.Packages.Search = "";
                    Assert.Equal(2, model.Packages.Entries.Count);

                    source.Fail = true;
                    await model.Packages.RefreshAsync();
                    Assert.Equal(2, model.Packages.Entries.Count);
                    Assert.Contains("previous evidence remains visible", model.Packages.Status, StringComparison.Ordinal);
                    Capture(window, "packages-source-failed.png");

                    model.ShowFilesCommand.Execute(null);
                    Assert.False(model.IsPackagesPage);
                    Assert.True(model.IsFilesPage);
                }
                finally { window.Close(); }

                source.Fail = false;
                await model.ShowPackagesCommand.ExecuteAsync(null);
                await model.Packages.RefreshAsync();
                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try { Capture(compact, "packages-compact.png"); }
                finally { compact.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }

    private sealed class FakePackageCatalog : IWorkspacePackageCatalogService
    {
        public bool Fail { get; set; }

        public Task<WorkspacePackageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Fail) throw new HttpRequestException("Endpoint unavailable");
            return Task.FromResult(new WorkspacePackageSnapshot(
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
                [new WorkspacePackageMetric("OfficeIMO.Word", "NuGet.org", "1.2.3", 1200,
                    "https://www.nuget.org/packages/OfficeIMO.Word"),
                 new WorkspacePackageMetric("PSPublishModule", "PowerShell Gallery", "3.0.144", 300,
                    "https://www.powershellgallery.com/packages/PSPublishModule")],
                1200, 300, 1, WorkspacePackageCatalogService.SourceUrl,
                SourceWarnings: ["PowerShell Gallery: upstream request failed."]));
        }
    }
}
