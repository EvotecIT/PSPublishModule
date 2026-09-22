using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceExplorerPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studio-state-contract-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_root, "workspace-roots.json");
    public WorkspaceExplorerPersistenceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void SessionAndLegacyProfileWritesPreserveEachOthersStateAndUnknownTopLevelFields()
    {
        File.WriteAllText(CatalogPath, "{\"futureSetting\":{\"value\":42}}");
        var store = new WorkspaceRootCatalogService(CatalogPath);
        var project = Path.Combine(_root, "Project");
        var reference = new WorkspaceDocumentReference(project, Path.Combine(project, "README.md"));
        store.SetFavorite(_root, project, true);
        store.SetProjectArchived(_root, project, true);
        store.SaveSession(_root, [reference], reference, [project]);
        store.SaveProfile(new WorkspaceProfile("release", "Release", null, null, null, [], _root, null, null, null));
        store.SaveActive(_root, "release");
        var restored = new WorkspaceRootCatalogService(CatalogPath);
        var state = restored.LoadExplorer(_root + Path.DirectorySeparatorChar);
        Assert.Equal(project, Assert.Single(state.FavoriteProjectRoots));
        Assert.Equal(project, Assert.Single(state.ArchivedProjectRoots!));
        Assert.Equal(reference, Assert.Single(state.OpenDocuments));
        Assert.Equal(reference, state.ActiveDocument);
        Assert.Equal("release", restored.Load(_root).ActiveProfileId);
        using var json = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        Assert.Equal(42, json.RootElement.GetProperty("futureSetting").GetProperty("value").GetInt32());
        Assert.Equal(project, json.RootElement.GetProperty("explorerStates")[0].GetProperty("archivedProjectRoots")[0].GetString());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentFavoritesAndSessionSavesDoNotLoseIndependentUpdates()
    {
        var first = new WorkspaceRootCatalogService(CatalogPath);
        var second = new WorkspaceRootCatalogService(CatalogPath);
        await Task.WhenAll(
            Task.Run(() => first.SetFavorite(_root, Path.Combine(_root, "First"), true)),
            Task.Run(() => second.SetFavorite(_root, Path.Combine(_root, "Second"), true)),
            Task.Run(() => second.SaveSession(_root, [], null, [_root])));
        Assert.Equal(2, first.LoadExplorer(_root).FavoriteProjectRoots.Count);
        Assert.Equal(_root, Assert.Single(first.LoadExplorer(_root).ExpandedPaths));
    }

    [Fact]
    public void PreferencesAreNormalizedAndPreserveExplorerStateAndUnknownFields()
    {
        File.WriteAllText(CatalogPath, "{\"futureSetting\":{\"value\":42}}");
        var store = new WorkspaceRootCatalogService(CatalogPath);
        var project = Path.Combine(_root, "Project");
        store.SetFavorite(_root, project, true);

        var saved = store.SavePreferences(new WorkspaceStudioPreferences(
            RestoreOpenDocuments: false,
            ActivityMaxGitHubRepositories: 99,
            ActivityMaxIssuesPerRepository: -1,
            ActivityMaxEntries: 4000,
            ActivityGitHubTimeoutSeconds: 2), _root);

        Assert.Equal(new WorkspaceStudioPreferences(false, 50, 0, 1000, 5), saved.Preferences);
        Assert.Equal(saved.Preferences, new WorkspaceRootCatalogService(CatalogPath).Load(_root).Preferences);
        Assert.Equal(project, Assert.Single(store.LoadExplorer(_root).FavoriteProjectRoots));
        using var json = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        Assert.Equal(42, json.RootElement.GetProperty("futureSetting").GetProperty("value").GetInt32());
        Assert.False(json.RootElement.GetProperty("preferences").GetProperty("restoreOpenDocuments").GetBoolean());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    [InlineData("{\"explorerStates\":[null]}")]
    public void CorruptCatalogIsNeverOverwrittenByEitherHost(string content)
    {
        File.WriteAllText(CatalogPath, content);
        var store = new WorkspaceRootCatalogService(CatalogPath);
        Assert.Throws<JsonException>(() => store.SetFavorite(_root, _root, true));
        Assert.Throws<JsonException>(() => store.SaveActive(_root));
        Assert.Equal(content, File.ReadAllText(CatalogPath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void InvalidDocumentContainmentCannotBePersistedAndFilesystemRootsRemainAbsolute()
    {
        var store = new WorkspaceRootCatalogService(CatalogPath);
        var reference = new WorkspaceDocumentReference(Path.Combine(_root, "Project"), Path.Combine(_root, "outside.txt"));
        Assert.Throws<ArgumentException>(() => store.SaveSession(_root, [reference], reference, []));
        Assert.False(File.Exists(CatalogPath));
        var filesystemRoot = Path.GetPathRoot(_root)!;
        Assert.Equal(filesystemRoot, PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(filesystemRoot));
    }

    [Fact]
    public void ForgetRecentRootPreservesFilesProfilesPreferencesAndExplorerState()
    {
        var active = Path.Combine(_root, "Active");
        var old = Path.Combine(_root, "Old");
        var profileRoot = Path.Combine(_root, "Profile");
        Directory.CreateDirectory(active);
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(profileRoot);
        var file = Path.Combine(old, "keep.txt");
        File.WriteAllText(file, "keep");
        File.WriteAllText(CatalogPath, "{\"futureSetting\":{\"value\":42}}");
        var store = new WorkspaceRootCatalogService(CatalogPath);
        store.SaveActive(old);
        store.SaveActive(profileRoot);
        store.SaveProfile(new WorkspaceProfile("retained", "Retained", null, null, null, [], profileRoot, null, null, null));
        store.SaveActive(active);
        store.SetFavorite(old, Path.Combine(old, "Project"), true);
        var preferences = new WorkspaceStudioPreferences(false, 4, 2, 90, 20);
        store.SavePreferences(preferences, active);
        var catalogJson = JsonNode.Parse(File.ReadAllText(CatalogPath))!.AsObject();
        catalogJson["preferences"]!.AsObject()["futurePreference"] = "keep preference";
        catalogJson["profiles"]!.AsArray()[0]!.AsObject()["futureProfile"] = "keep profile";
        catalogJson["explorerStates"]!.AsArray()[0]!.AsObject()["futureExplorer"] = "keep explorer";
        File.WriteAllText(CatalogPath, catalogJson.ToJsonString());

        var before = store.Load(active);
        Assert.Equal(3, before.RecentWorkspaceRoots.Count);
        var beforeJson = File.ReadAllText(CatalogPath);
        Assert.Throws<InvalidOperationException>(() => store.ForgetRecentRoot(active, active));
        Assert.Throws<InvalidOperationException>(() => store.ForgetRecentRoot(profileRoot, active));
        Assert.Equal(beforeJson, File.ReadAllText(CatalogPath));

        var result = store.ForgetRecentRoot(old + Path.DirectorySeparatorChar, active);
        Assert.Equal(active, result.ActiveWorkspaceRoot);
        Assert.Equal([active, profileRoot], result.RecentWorkspaceRoots);
        Assert.Equal(preferences, result.Preferences);
        Assert.Equal("retained", Assert.Single(result.Profiles).ProfileId);
        Assert.Equal("keep", File.ReadAllText(file));
        Assert.Equal(Path.Combine(old, "Project"), Assert.Single(store.LoadExplorer(old).FavoriteProjectRoots));
        using var json = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        Assert.Equal(42, json.RootElement.GetProperty("futureSetting").GetProperty("value").GetInt32());
        Assert.Equal("keep preference", json.RootElement.GetProperty("preferences").GetProperty("futurePreference").GetString());
        Assert.Equal("keep profile", json.RootElement.GetProperty("profiles")[0].GetProperty("futureProfile").GetString());
        Assert.Equal("keep explorer", json.RootElement.GetProperty("explorerStates")[0].GetProperty("futureExplorer").GetString());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        var reopened = new WorkspaceRootCatalogService(CatalogPath).Load(active);
        Assert.Equal(result.ActiveWorkspaceRoot, reopened.ActiveWorkspaceRoot);
        Assert.Equal(result.RecentWorkspaceRoots, reopened.RecentWorkspaceRoots);
        Assert.Equal(result.Preferences, reopened.Preferences);
    }
}
