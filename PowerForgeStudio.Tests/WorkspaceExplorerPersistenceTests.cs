using System.Text.Json;
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
        store.SaveSession(_root, [reference], reference, [project]);
        store.SaveProfile(new WorkspaceProfile("release", "Release", null, null, null, [], _root, null, null, null));
        store.SaveActive(_root, "release");
        var restored = new WorkspaceRootCatalogService(CatalogPath);
        var state = restored.LoadExplorer(_root + Path.DirectorySeparatorChar);
        Assert.Equal(project, Assert.Single(state.FavoriteProjectRoots));
        Assert.Equal(reference, Assert.Single(state.OpenDocuments));
        Assert.Equal(reference, state.ActiveDocument);
        Assert.Equal("release", restored.Load(_root).ActiveProfileId);
        using var json = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        Assert.Equal(42, json.RootElement.GetProperty("futureSetting").GetProperty("value").GetInt32());
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
}
