using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Packages;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Orchestrator.Packages;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspacePackagesTests
{
    [Fact]
    public async Task SavedPublicationOpensExactPackageInPublicSnapshotWithoutClaimingVersionDownloads()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-package-handoff-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var source = new FakePackageCatalog();
                using var model = new WorkspaceViewModel(root, packages: source);
                model.ShowReleaseCommand.Execute(null);
                var receipt = new ReleasePublishReceipt(root, "OfficeIMO", "ProjectBuild", "signed-output.nupkg", "NuGet",
                    "https://api.nuget.org/v3/index.json", "signed-output.nupkg", ReleasePublishReceiptStatus.Published,
                    "Published.", DateTimeOffset.UtcNow) { PackageId = "OfficeIMO.Word", PackageVersion = "1.2.3" };
                model.Release.PublicationReceipts.Add(receipt);
                model.Release.SelectedPublicationReceipt = receipt;

                var releaseWindow = new MainWindow { DataContext = model, Width = 1600, Height = 900 };
                releaseWindow.Show();
                try
                {
                    releaseWindow.GetVisualDescendants().OfType<ReleaseView>().Single()
                        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(releaseWindow, "package-release-receipt-wide.png");
                    releaseWindow.Width = 1050;
                    releaseWindow.Height = 720;
                    releaseWindow.UpdateLayout();
                    releaseWindow.GetVisualDescendants().OfType<ReleaseView>().Single()
                        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(releaseWindow, "package-release-receipt-compact.png");
                }
                finally { releaseWindow.Close(); }

                await model.Release.InspectPublicPackageCommand.ExecuteAsync(null);

                Assert.True(model.IsPackagesPage);
                Assert.Equal("OfficeIMO.Word", model.Packages.Search);
                Assert.Equal("NuGet.org", model.Packages.Filter);
                Assert.Equal("OfficeIMO.Word", model.Packages.SelectedEntry?.Id);
                Assert.Contains("latest version", model.Packages.HandoffStatus, StringComparison.Ordinal);
                Assert.Contains("package-wide", model.Packages.HandoffStatus, StringComparison.Ordinal);

                var wide = new MainWindow { DataContext = model, Width = 1600, Height = 900 };
                wide.Show();
                try { Capture(wide, "package-release-handoff-wide.png"); }
                finally { wide.Close(); }
                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try { Capture(compact, "package-release-handoff-compact.png"); }
                finally { compact.Close(); }

                var privateReceipt = receipt with { Destination = "https://private.example.test/v3/index.json" };
                Assert.False(privateReceipt.CanInspectPublicPackage);
                Assert.False((receipt with { TargetKind = "PowerShellRepository", Destination = "PowerShellGallery" }).CanInspectPublicPackage);
                Assert.Equal("PowerShell Gallery", (receipt with { TargetKind = "PowerShellRepository", Destination = "PSGallery" }).PublicRegistry);
                Assert.False((receipt with { Status = ReleasePublishReceiptStatus.Failed }).CanInspectPublicPackage);

                source.NuGetVersion = "1.2.4";
                await model.Packages.RefreshAsync();
                Assert.Equal("1.2.4", model.Packages.SelectedEntry?.LatestVersion);
                Assert.Equal("", model.Packages.HandoffStatus);
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
        public string NuGetVersion { get; set; } = "1.2.3";

        public Task<WorkspacePackageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Fail) throw new HttpRequestException("Endpoint unavailable");
            return Task.FromResult(new WorkspacePackageSnapshot(
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
                [new WorkspacePackageMetric("OfficeIMO.Word", "NuGet.org", NuGetVersion, 1200,
                    "https://www.nuget.org/packages/OfficeIMO.Word"),
                 new WorkspacePackageMetric("PSPublishModule", "PowerShell Gallery", "3.0.144", 300,
                    "https://www.powershellgallery.com/packages/PSPublishModule")],
                1200, 300, 1, WorkspacePackageCatalogService.SourceUrl,
                SourceWarnings: ["PowerShell Gallery: upstream request failed."]));
        }
    }
}
