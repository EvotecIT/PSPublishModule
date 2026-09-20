using PowerForge;
using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

/// <summary>Combines read-only provider checks while preserving provider ownership and failure isolation.</summary>
public sealed class WorkspaceConnectionInventoryService : IWorkspaceConnectionInventoryService
{
    private readonly IReadOnlyList<IWorkspaceConnectionSource> _sources;

    public WorkspaceConnectionInventoryService(IProcessRunner? processRunner = null)
        : this([
            new ToolchainConnectionSource(processRunner),
            new GitHubConnectionSource(processRunner),
            new RegistryConnectionSource(),
            new LicensingConnectionSource(),
            new IntelligenceXConnectionSource()
        ]) { }

    internal WorkspaceConnectionInventoryService(IReadOnlyList<IWorkspaceConnectionSource> sources)
        => _sources = sources;

    public async Task<WorkspaceConnectionSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var reads = _sources.Select(source => ReadSafelyAsync(source, root, cancellationToken)).ToArray();
        var results = await Task.WhenAll(reads).ConfigureAwait(false);
        var entries = results.SelectMany(static result => result.Entries)
            .OrderBy(static entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new WorkspaceConnectionSnapshot(DateTimeOffset.UtcNow, entries, results.Select(static result => result.State).ToArray());
    }

    private static async Task<ConnectionSourceResult> ReadSafelyAsync(
        IWorkspaceConnectionSource source,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ConnectionSourceResult([], new WorkspaceConnectionSourceState(
                source.Provider, "Unavailable", 0, "Provider evidence could not be read."));
        }
    }
}
