using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf.Tests;

public sealed class WorkspaceRootCatalogServiceTests
{
    [Fact]
    public void Load_UsesFallbackWorkspaceRoot_WhenCatalogDoesNotExist()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace-roots.json");
        var service = new WorkspaceRootCatalogService(catalogPath);

        var catalog = service.Load(@"C:\Support\GitHub");

        Assert.Equal(@"C:\Support\GitHub", catalog.ActiveWorkspaceRoot);
        Assert.Equal(@"C:\Support\GitHub", Assert.Single(catalog.RecentWorkspaceRoots));
        Assert.Null(catalog.ActiveProfileId);
        Assert.Empty(catalog.Profiles);
    }

    [Fact]
    public void SaveActive_PersistsRecentWorkspaceRootsWithoutDuplicates()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var catalogPath = Path.Combine(testRoot, "workspace-roots.json");
        var service = new WorkspaceRootCatalogService(catalogPath);

        var first = service.SaveActive(@"C:\Support\GitHub");
        var second = service.SaveActive(@"D:\Repos\Studio");
        var reloaded = service.Load(@"C:\Fallback");

        Assert.Equal(@"D:\Repos\Studio", second.RecentWorkspaceRoots[0]);
        Assert.Equal(@"D:\Repos\Studio", reloaded.ActiveWorkspaceRoot);
        Assert.Equal(2, reloaded.RecentWorkspaceRoots.Count);
        Assert.Equal(@"D:\Repos\Studio", reloaded.RecentWorkspaceRoots[0]);
        Assert.Equal(@"C:\Support\GitHub", reloaded.RecentWorkspaceRoots[1]);
        Assert.Empty(reloaded.Profiles);
        Assert.True(File.Exists(catalogPath));
    }

    [Fact]
    public void Load_IgnoresInvalidJsonAndFallsBackGracefully()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var catalogPath = Path.Combine(testRoot, "workspace-roots.json");
        File.WriteAllText(catalogPath, "{ not-json");

        var service = new WorkspaceRootCatalogService(catalogPath);
        var catalog = service.Load(@"C:\Support\GitHub");

        Assert.Equal(@"C:\Support\GitHub", catalog.ActiveWorkspaceRoot);
        Assert.Equal(@"C:\Support\GitHub", Assert.Single(catalog.RecentWorkspaceRoots));
        Assert.Empty(catalog.Profiles);
    }

    [Fact]
    public void SaveProfile_PersistsProfilesAndTracksActiveProfile()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var catalogPath = Path.Combine(testRoot, "workspace-roots.json");
        var service = new WorkspaceRootCatalogService(catalogPath);
        var executedAtUtc = DateTimeOffset.UtcNow;

        var saved = service.SaveProfile(new WorkspaceProfile(
            ProfileId: "modules",
            DisplayName: "Modules",
            Description: "Daily module release desk",
            TodayNote: "Ship PSDscResources and verify gallery receipts.",
            LastLaunchResult: new WorkspaceProfileLaunchResult(
                WorkspaceProfileLaunchActionKind.PrepareQueue,
                "Prepare Queue",
                true,
                "Prepared the Alpha family queue.",
                executedAtUtc),
            LaunchHistory: [
                new WorkspaceProfileLaunchResult(
                    WorkspaceProfileLaunchActionKind.PrepareQueue,
                    "Prepare Queue",
                    true,
                    "Prepared the Alpha family queue.",
                    executedAtUtc),
                new WorkspaceProfileLaunchResult(
                    WorkspaceProfileLaunchActionKind.ApplySavedView,
                    "Apply Saved View",
                    true,
                    "Applied saved portfolio view 'Ready Today'.",
                    executedAtUtc.AddMinutes(-15))
            ],
            WorkspaceRoot: @"D:\Repos\Modules",
            SavedViewId: "ready-today",
            QueueScopeKey: "alpha",
            QueueScopeDisplayName: "Alpha",
            PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Attention,
            PreferredStartupSearchText: "Repo.Alpha",
            PreferredStartupFamilyKey: "alpha",
            PreferredStartupFamilyDisplayName: "Alpha",
            ApplyStartupPreferenceAfterSavedView: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow), activeProfileId: "modules");
        var reloaded = service.Load(@"C:\Fallback");

        Assert.Equal("modules", saved.ActiveProfileId);
        var profile = Assert.Single(reloaded.Profiles);
        Assert.Equal("Modules", profile.DisplayName);
        Assert.Equal("Daily module release desk", profile.Description);
        Assert.Equal("Ship PSDscResources and verify gallery receipts.", profile.TodayNote);
        Assert.NotNull(profile.LastLaunchResult);
        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, profile.LastLaunchResult?.ActionKind);
        Assert.Equal("Prepared the Alpha family queue.", profile.LastLaunchResult?.Summary);
        Assert.Equal(2, profile.LaunchHistory.Count);
        Assert.Equal(@"D:\Repos\Modules", profile.WorkspaceRoot);
        Assert.Equal("ready-today", profile.SavedViewId);
        Assert.Equal("alpha", profile.QueueScopeKey);
        Assert.Equal(RepositoryPortfolioFocusMode.Attention, profile.PreferredStartupFocusMode);
        Assert.Equal("Repo.Alpha", profile.PreferredStartupSearchText);
        Assert.Equal("alpha", profile.PreferredStartupFamilyKey);
        Assert.Equal("Alpha", profile.PreferredStartupFamilyDisplayName);
        Assert.True(profile.ApplyStartupPreferenceAfterSavedView);
        Assert.Equal("modules", reloaded.ActiveProfileId);
        Assert.Equal(@"D:\Repos\Modules", reloaded.ActiveWorkspaceRoot);
    }

    [Fact]
    public void DeleteProfile_RemovesDeletedProfileAndClearsActiveSelection()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var catalogPath = Path.Combine(testRoot, "workspace-roots.json");
        var service = new WorkspaceRootCatalogService(catalogPath);

        service.SaveProfile(new WorkspaceProfile(
            ProfileId: "modules",
            DisplayName: "Modules",
            Description: "Daily module release desk",
            TodayNote: "Ship PSDscResources and verify gallery receipts.",
            LastLaunchResult: null,
            LaunchHistory: [],
            WorkspaceRoot: @"D:\Repos\Modules",
            SavedViewId: "ready-today",
            QueueScopeKey: "alpha",
            QueueScopeDisplayName: "Alpha",
            UpdatedAtUtc: DateTimeOffset.UtcNow), activeProfileId: "modules");

        var updated = service.DeleteProfile("modules", @"C:\Support\GitHub", activeProfileId: "modules");

        Assert.Null(updated.ActiveProfileId);
        Assert.Empty(updated.Profiles);
    }

    [Fact]
    public void SaveTemplate_PersistsCustomTemplatesAndDeleteTemplateRemovesThem()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var catalogPath = Path.Combine(testRoot, "workspace-roots.json");
        var service = new WorkspaceRootCatalogService(catalogPath);

        var saved = service.SaveTemplate(
            new WorkspaceProfileTemplate(
                TemplateId: "template-modules",
                DisplayName: "Modules Custom",
                Summary: "Custom module desk.",
                Description: "Custom module release desk",
                TodayNote: "Start with the ready family and verify receipts.",
                PreferredActionKinds: [
                    WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                    WorkspaceProfileLaunchActionKind.PrepareQueue
                ],
                PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Ready,
                PreferredStartupFamily: "Alpha",
                ApplyStartupPreferenceAfterSavedView: true,
                PreferCurrentFamilyForQueueScope: true),
            @"D:\Repos\Modules");
        var reloaded = service.Load(@"C:\Fallback");

        var template = Assert.Single(reloaded.Templates!);
        Assert.Equal("Modules Custom", template.DisplayName);
        Assert.Equal("Custom module desk.", template.Summary);
        Assert.Equal("Custom module release desk", template.Description);
        Assert.Equal("Alpha", template.PreferredStartupFamily);
        Assert.True(template.ApplyStartupPreferenceAfterSavedView);
        Assert.True(template.PreferCurrentFamilyForQueueScope);
        Assert.False(template.IsBuiltIn);
        Assert.Equal(@"D:\Repos\Modules", saved.ActiveWorkspaceRoot);

        var deleted = service.DeleteTemplate("template-modules", @"D:\Repos\Modules");

        Assert.Empty(deleted.Templates!);
    }
}
