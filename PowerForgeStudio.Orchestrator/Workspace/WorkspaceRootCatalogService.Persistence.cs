using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed partial class WorkspaceRootCatalogService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public WorkspaceExplorerState LoadExplorer(string workspaceRoot)
        => FindExplorer(LoadDocument(strict: true), NormalizeRoot(workspaceRoot));

    public WorkspaceExplorerState SetFavorite(string workspaceRoot, string projectRoot, bool favorite)
    {
        workspaceRoot = NormalizeRoot(workspaceRoot);
        projectRoot = NormalizeRoot(projectRoot);
        using var writeLock = AcquireWriteLock();
        var document = LoadDocument(strict: true) ?? EmptyDocument(workspaceRoot);
        var state = FindExplorer(document, workspaceRoot);
        var favorites = state.FavoriteProjectRoots.Where(path => !PathComparer.Equals(path, projectRoot)).ToList();
        if (favorite) favorites.Add(projectRoot);
        state = state with { FavoriteProjectRoots = favorites };
        PersistExplorer(document, state);
        return state;
    }

    public WorkspaceExplorerState SaveSession(string workspaceRoot, IReadOnlyList<WorkspaceDocumentReference> documents,
        WorkspaceDocumentReference? activeDocument, IReadOnlyList<string> expandedPaths)
    {
        workspaceRoot = NormalizeRoot(workspaceRoot);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(expandedPaths);
        if (documents.Any(reference => !ValidDocument(reference)) || activeDocument is not null && !ValidDocument(activeDocument))
            throw new ArgumentException("Document paths must be absolute and inside their working copy.", nameof(documents));
        using var writeLock = AcquireWriteLock();
        var document = LoadDocument(strict: true) ?? EmptyDocument(workspaceRoot);
        var state = FindExplorer(document, workspaceRoot) with
        {
            OpenDocuments = documents.ToArray(), ActiveDocument = activeDocument,
            ExpandedPaths = expandedPaths.Select(NormalizeRoot).Distinct(PathComparer).ToArray()
        };
        PersistExplorer(document, state);
        return state;
    }

    private static WorkspaceExplorerState FindExplorer(WorkspaceRootCatalogDocument? document, string root)
        => document?.ExplorerStates?.FirstOrDefault(state => PathComparer.Equals(state.WorkspaceRoot, root))
           ?? new WorkspaceExplorerState(root, [], [], null, []);

    private static string NormalizeRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private void PersistExplorer(WorkspaceRootCatalogDocument document, WorkspaceExplorerState state)
    {
        var states = (document.ExplorerStates ?? []).Where(existing => !PathComparer.Equals(existing.WorkspaceRoot, state.WorkspaceRoot)).ToList();
        states.Add(state);
        WriteDocument(document with { ExplorerStates = states, UpdatedAtUtc = DateTimeOffset.UtcNow });
    }

    private WorkspaceRootCatalogDocument? LoadDocument(bool strict = false)
    {
        try
        {
            using var stream = new FileStream(_catalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = JsonSerializer.Deserialize<WorkspaceRootCatalogDocument>(stream, SerializerOptions)
                ?? throw new JsonException("The workspace catalog must contain a JSON object.");
            foreach (var state in document.ExplorerStates ?? [])
            {
                if (state is null || string.IsNullOrWhiteSpace(state.WorkspaceRoot) || !Path.IsPathFullyQualified(state.WorkspaceRoot)
                    || state.FavoriteProjectRoots is null || state.OpenDocuments is null || state.ExpandedPaths is null
                    || state.FavoriteProjectRoots.Concat(state.ExpandedPaths).Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                    || state.OpenDocuments.Any(reference => !ValidDocument(reference)) || state.ActiveDocument is not null && !ValidDocument(state.ActiveDocument))
                    throw new JsonException("The saved explorer state contains invalid paths or missing collections.");
            }
            return document;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) when (!strict) { return null; }
        catch (IOException) when (!strict) { return null; }
    }

    private static bool ValidDocument(WorkspaceDocumentReference? reference)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.Path) || string.IsNullOrWhiteSpace(reference.WorkingCopyRoot)
            || !Path.IsPathFullyQualified(reference.Path) || !Path.IsPathFullyQualified(reference.WorkingCopyRoot)) return false;
        var relative = Path.GetRelativePath(reference.WorkingCopyRoot, reference.Path);
        return relative != "." && relative != ".." && !Path.IsPathRooted(relative) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private FileStream AcquireWriteLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(_catalogPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 40) { Thread.Sleep(50); }
        }
    }

    private static WorkspaceRootCatalogDocument EmptyDocument(string root)
        => new(root, [root], null, [], [], DateTimeOffset.UtcNow, []);

    private void PersistCatalog(WorkspaceRootCatalog catalog)
    {
        var existing = LoadDocument(strict: true) ?? EmptyDocument(catalog.ActiveWorkspaceRoot);
        WriteDocument(existing with
        {
            ActiveWorkspaceRoot = catalog.ActiveWorkspaceRoot, RecentWorkspaceRoots = catalog.RecentWorkspaceRoots,
            ActiveProfileId = catalog.ActiveProfileId, Profiles = catalog.Profiles, Templates = catalog.Templates,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void WriteDocument(WorkspaceRootCatalogDocument document)
    {
        var temporary = _catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _catalogPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record WorkspaceRootCatalogDocument(
        string? ActiveWorkspaceRoot, IReadOnlyList<string>? RecentWorkspaceRoots, string? ActiveProfileId,
        IReadOnlyList<WorkspaceProfile>? Profiles, IReadOnlyList<WorkspaceProfileTemplate>? Templates,
        DateTimeOffset UpdatedAtUtc, IReadOnlyList<WorkspaceExplorerState>? ExplorerStates = null)
    {
        [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
    }
}
