using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal sealed class RegistryConnectionSource(HttpClient? client = null) : IWorkspaceConnectionSource
{
    public string Provider => "Package registries";

    public async Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        RegistryProbe[] probes =
        [
            new("nuget", "NuGet.org", "https://api.nuget.org/v3/index.json"),
            new("psgallery", "PowerShell Gallery", "https://www.powershellgallery.com/api/v2/")
        ];
        var entries = await Task.WhenAll(probes.Select(probe => InspectAsync(probe, cancellationToken))).ConfigureAwait(false);
        var reachable = entries.Count(static entry => entry.IsVerified);
        return new ConnectionSourceResult(entries, new WorkspaceConnectionSourceState(
            Provider, reachable == entries.Length ? "Available" : reachable == 0 ? "Unavailable" : "Partial", entries.Length,
            $"{reachable} of {entries.Length} public registry endpoints responded. Publishing credentials were not read."));
    }

    private async Task<WorkspaceConnectionEntry> InspectAsync(RegistryProbe probe, CancellationToken token)
    {
        var reachable = false;
        try { reachable = await HttpConnectionSupport.IsReachableAsync(probe.Endpoint, token, client).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }
        return new WorkspaceConnectionEntry(
            "registry:" + probe.Id, probe.Name, Provider, "Registries", reachable ? "Reachable" : "Unavailable",
            ConnectionEndpointSanitizer.Sanitize(probe.Endpoint), "External publish credential; not loaded", reachable ? ["Public service index"] : [],
            reachable ? DateTimeOffset.UtcNow : null,
            reachable ? "Public endpoint responded. Authenticated publish scope was not tested." : "Public endpoint did not respond.",
            probe.Name);
    }

    private sealed record RegistryProbe(string Id, string Name, string Endpoint);
}
