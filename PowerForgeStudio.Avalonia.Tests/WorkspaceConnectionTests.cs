using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Connections;
using PowerForgeStudio.Orchestrator.Connections;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceConnectionTests
{
    [Fact]
    public void LicensingHandoffOpensOnlyTheKnownPortalWithoutPassingCredentials()
    {
        var opened = new List<string>();
        using var model = new ConnectionsViewModel(openExternal: opened.Add);
        var entry = new WorkspaceConnectionEntry("licensing:control", "Evotec Control", "Licensing", "Product services",
            "Unconfigured", "https://control.evotec.xyz", "Protected profile reference", [], null,
            "Public health only", "Licensing.Admin");
        model.SelectedEntry = entry;
        Assert.True(model.CanOpenSelectedPortal);
        model.OpenSelectedPortalCommand.Execute(null);
        Assert.Equal(["https://control.evotec.xyz/"], opened);
        Assert.DoesNotContain("Protected profile", model.Status, StringComparison.Ordinal);

        foreach (var endpoint in new[] { "https://control.evotec.xyz.evil.invalid", "https://control.evotec.xyz/?token=secret", "http://control.evotec.xyz" })
        {
            model.SelectedEntry = entry with { Endpoint = endpoint };
            Assert.False(model.CanOpenSelectedPortal);
            Assert.False(model.OpenSelectedPortalCommand.CanExecute(null));
        }
        Assert.Single(opened);
    }

    [Fact]
    public async Task WorkspaceChangeCancelsInspectionAndAllowsImmediateRefresh()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-connection-switch-" + Guid.NewGuid().ToString("N"))).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "Other")).FullName;
        try
        {
            var inventory = new ControlledConnectionInventory();
            using var model = new ConnectionsViewModel(inventory);
            model.SetWorkspace(root);
            var first = model.RefreshAsync();
            await inventory.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            model.SetWorkspace(other);
            Assert.False(model.IsLoading);
            await model.RefreshAsync();
            await first;

            Assert.Equal(2, inventory.Calls);
            Assert.Contains("Verified 0 connection boundaries", model.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConnectionsRouteShowsSecretFreeProviderEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-connection-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "PowerForge")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root, connections: new FakeConnectionInventory());
                await model.RefreshAsync();
                await model.ShowConnectionsCommand.ExecuteAsync(null);
                Assert.True(model.IsConnectionsPage);
                Assert.True(model.IsWorkspaceUtilityPage);
                Assert.Equal(8, model.Connections.Entries.Count);
                Assert.Equal(5, model.Connections.Sources.Count);
                Assert.Equal(5, model.Connections.VerifiedCount);
                Assert.Equal(1, model.Connections.AvailableCount);
                Assert.Equal(1, model.Connections.UnconfiguredCount);
                Assert.Equal(2, model.Connections.PendingCount);
                Assert.Equal(1, model.Connections.AttentionCount);
                Assert.DoesNotContain("secret-value", model.Connections.Output, StringComparison.Ordinal);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    model.Connections.SelectedEntry = model.Connections.Entries[0];
                    Capture(window, "connections-inventory.png");
                    model.Connections.SelectedEntry = model.Connections.Entries.Single(entry => entry.Provider == "Licensing");
                    Assert.True(model.Connections.CanOpenSelectedPortal);
                    Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                        button.Content is string content && content == "Open Evotec Control" && button.IsEffectivelyVisible);
                    Capture(window, "connections-licensing.png");
                    model.Connections.ShowToolchainsCommand.Execute(null);
                    Assert.Equal(3, model.Connections.Entries.Count);
                    model.Connections.ShowAttentionCommand.Execute(null);
                    Assert.Equal("Authentication required", Assert.Single(model.Connections.Entries).State);
                    model.Connections.ShowAllCommand.Execute(null);
                }
                finally { window.Close(); }

                model.Connections.SelectedEntry = model.Connections.Entries.Single(entry => entry.Provider == "Licensing");
                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try
                {
                    Capture(compact, "connections-inventory-compact.png");
                    var details = compact.FindControl<Button>("CompactContextButton")!;
                    details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Contains(compact.GetVisualDescendants().OfType<Button>(), button =>
                        button.Content is string content && content == "Open Evotec Control" && button.IsEffectivelyVisible);
                    Capture(compact, "connections-licensing-compact-details.png");
                    details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var page = compact.FindControl<ConnectionsView>("ConnectionsPage");
                    var scroller = page?.FindControl<global::Avalonia.Controls.ScrollViewer>("PageScroll");
                    Assert.NotNull(scroller);
                    scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
                    Capture(compact, "connections-inventory-compact-list.png");
                }
                finally { compact.Close(); }
                return true;
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Capture(MainWindow window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, fileName), PngBitmapEncoderOptions.Default);
    }

    private sealed class FakeConnectionInventory : IWorkspaceConnectionInventoryService
    {
        public Task<WorkspaceConnectionSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            WorkspaceConnectionEntry[] entries =
            [
                Entry("git", "Git", "Local toolchains", "Toolchains", "Verified", "Local process", "No credential required", ["Repositories", "Worktrees"], now),
                Entry("dotnet", ".NET SDK", "Local toolchains", "Toolchains", "Verified", "Local process", "No credential required", ["Build", "Test", "NuGet pack"], now),
                Entry("pwsh", "PowerShell", "Local toolchains", "Toolchains", "Verified", "Local process", "No credential required", ["Module builds", "Scripts"], now),
                Entry("github", "GitHub", "GitHub", "Source control", "Authentication required", "https://github.com", "GitHub CLI credential store", ["GitHub CLI available"], null),
                Entry("nuget", "NuGet.org", "Package registries", "Registries", "Reachable", "https://api.nuget.org/v3/index.json", "External publish credential; not loaded", ["Public service index"], now),
                Entry("gallery", "PowerShell Gallery", "Package registries", "Registries", "Reachable", "https://www.powershellgallery.com/api/v2", "External publish credential; not loaded", ["Public service index"], now),
                Entry("licensing:control", "Evotec Control", "Licensing", "Product services", "Unconfigured", "https://control.evotec.xyz", "No Licensing.Admin protected-profile reference found", ["Public health"], null),
                Entry("ix", "IntelligenceX", "IntelligenceX", "Intelligence", "Available", "named-pipe://./intelligencex.chat", "IntelligenceX provider-owned profile store", ["Owner source detected"], null)
            ];
            WorkspaceConnectionSourceState[] sources =
            [
                new("Local toolchains", "Available", 3, "Installed tools responded to version checks."),
                new("GitHub", "Authentication required", 1, "No active account was confirmed."),
                new("Package registries", "Available", 2, "Public endpoints responded."),
                new("Licensing", "Unconfigured", 1, "Public health is reachable; no profile reference."),
                new("IntelligenceX", "Available", 1, "Owner available; service handshake not active in this fixture.")
            ];
            return Task.FromResult(new WorkspaceConnectionSnapshot(now, entries, sources));
        }

        private static WorkspaceConnectionEntry Entry(string id, string name, string provider, string category,
            string state, string endpoint, string credential, IReadOnlyList<string> capabilities, DateTimeOffset? verified)
            => new(id, name, provider, category, state, endpoint, credential, capabilities, verified,
                "Read-only evidence. No secret values were loaded.", provider);
    }

    private sealed class ControlledConnectionInventory : IWorkspaceConnectionInventoryService
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<WorkspaceConnectionSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
            {
                FirstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new WorkspaceConnectionSnapshot(DateTimeOffset.UtcNow, [],
                [new WorkspaceConnectionSourceState("Test", "Available", 0, "No connections")]);
        }
    }
}
