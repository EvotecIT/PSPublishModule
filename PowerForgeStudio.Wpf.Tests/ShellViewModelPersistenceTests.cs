using System.IO;

namespace PowerForgeStudio.Wpf.Tests;

public sealed partial class ShellViewModelTests
{
    [Fact]
    public async Task CatalogWriteFailureIsVisibleAndDoesNotSwitchWorkspaceOrDestroySettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "studio-wpf-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "workspace-roots.json");
            var originalRoot = Path.Combine(directory, "Original");
            Directory.CreateDirectory(originalRoot);
            var profile = new WorkspaceProfile("other", "Other", null, null, null, [], Path.Combine(directory, "Other"), null, null, null);
            var store = new WorkspaceRootCatalogService(catalogPath);
            store.SaveProfile(profile);
            store.SaveActive(originalRoot);
            var model = CreateViewModel(CreateSnapshot(), workspaceRootCatalogService: store);
            File.WriteAllText(catalogPath, "{broken-json");

            await model.RefreshAsync(forceRefresh: true);
            Assert.False(model.IsBusy);
            Assert.Contains("Could not save workspace settings", model.StatusText);
            model.PortfolioOverview.SelectedWorkspaceProfile = Assert.Single(model.PortfolioOverview.WorkspaceProfiles);
            model.ApplyWorkspaceProfileCommand.Execute(null);
            Assert.Equal(originalRoot, model.WorkspaceRoot);
            Assert.Contains("Could not save workspace settings", model.StatusText);
            model.PortfolioOverview.WorkspaceProfileDraftName = "New profile";
            model.SaveWorkspaceProfileCommand.Execute(null);
            Assert.Contains("Could not save workspace settings", model.StatusText);
            Assert.False(model.IsBusy);
            Assert.Equal("{broken-json", File.ReadAllText(catalogPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
