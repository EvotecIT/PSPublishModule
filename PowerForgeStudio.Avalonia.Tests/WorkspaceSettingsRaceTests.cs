using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceSettingsRaceTests
{
    [Fact]
    public async Task ForgettingARootDoesNotDiscardPreferencesEditedWhileItsWriteIsRunning()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var active = Path.Combine(fixture, "Active");
            var old = Path.Combine(fixture, "Old");
            var store = new WorkspaceRootCatalogService(Path.Combine(fixture, "catalog.json"));
            store.SaveActive(old);
            store.SaveActive(active);
            var delayed = new DelayedRootCatalog(store);
            var model = new SettingsViewModel(delayed, store);
            model.SetWorkspace(active);
            var root = Assert.Single(model.WorkspaceRoots, item => item.Path == old);

            var forgetting = model.ForgetRootCommand.ExecuteAsync(root);
            try
            {
                await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                model.ActivityMaxEntries = 130;
            }
            finally { delayed.Release.TrySetResult(); }
            await forgetting;

            Assert.Equal(130, model.ActivityMaxEntries);
            Assert.True(model.IsDirty);
            Assert.Equal(WorkspaceStudioPreferences.Default, model.SavedPreferences);
            Assert.Single(model.WorkspaceRoots);
            Assert.DoesNotContain(old, store.Load(active).RecentWorkspaceRoots);
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task SaveDoesNotDiscardNewerPreferencesEditedWhileItsWriteIsRunning()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-settings-save-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var store = new WorkspaceRootCatalogService(Path.Combine(fixture, "catalog.json"));
            store.SaveActive(fixture);
            var delayed = new DelayedPreferences(store);
            var model = new SettingsViewModel(store, delayed);
            model.SetWorkspace(fixture);
            model.ActivityMaxEntries = 130;

            var saving = model.SaveCommand.ExecuteAsync(null);
            try
            {
                await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                model.ActivityMaxEntries = 140;
            }
            finally { delayed.Release.TrySetResult(); }
            await saving;

            Assert.Equal(140, model.ActivityMaxEntries);
            Assert.Equal(130, model.SavedPreferences.ActivityMaxEntries);
            Assert.Equal(130, store.Load(fixture).Preferences!.ActivityMaxEntries);
            Assert.True(model.IsDirty);
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    private sealed class DelayedRootCatalog(WorkspaceRootCatalogService inner) : IWorkspaceRootCatalogService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WorkspaceRootCatalog Load(string fallbackWorkspaceRoot) => inner.Load(fallbackWorkspaceRoot);
        public WorkspaceRootCatalog SaveActive(string workspaceRoot, string? activeProfileId = null) => inner.SaveActive(workspaceRoot, activeProfileId);
        public WorkspaceRootCatalog SaveProfile(WorkspaceProfile profile, string? activeProfileId = null) => inner.SaveProfile(profile, activeProfileId);
        public WorkspaceRootCatalog DeleteProfile(string profileId, string fallbackWorkspaceRoot, string? activeProfileId = null) => inner.DeleteProfile(profileId, fallbackWorkspaceRoot, activeProfileId);
        public WorkspaceRootCatalog SaveTemplate(WorkspaceProfileTemplate template, string fallbackWorkspaceRoot, string? activeProfileId = null) => inner.SaveTemplate(template, fallbackWorkspaceRoot, activeProfileId);
        public WorkspaceRootCatalog DeleteTemplate(string templateId, string fallbackWorkspaceRoot, string? activeProfileId = null) => inner.DeleteTemplate(templateId, fallbackWorkspaceRoot, activeProfileId);
        public WorkspaceRootCatalog ForgetRecentRoot(string workspaceRoot, string fallbackWorkspaceRoot)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return inner.ForgetRecentRoot(workspaceRoot, fallbackWorkspaceRoot);
        }
    }

    private sealed class DelayedPreferences(WorkspaceRootCatalogService inner) : IWorkspacePreferenceService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ConfigurationPath => inner.ConfigurationPath;
        public WorkspaceRootCatalog SavePreferences(WorkspaceStudioPreferences preferences, string fallbackWorkspaceRoot, string? activeProfileId = null)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return inner.SavePreferences(preferences, fallbackWorkspaceRoot, activeProfileId);
        }
    }
}
