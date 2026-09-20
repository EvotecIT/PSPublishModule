using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal sealed class LicensingConnectionSource(HttpClient? client = null, string? profileRoot = null) : IWorkspaceConnectionSource
{
    private const string PortalEndpoint = "https://control.evotec.xyz";
    public string Provider => "Licensing";

    public async Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var root = profileRoot ?? DefaultProfileRoot();
        var profileCount = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).Take(101).Count()
            : 0;
        var reachable = false;
        try { reachable = await HttpConnectionSupport.IsReachableAsync(PortalEndpoint + "/healthz", cancellationToken, client).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }
        var credential = profileCount switch
        {
            0 => "No Licensing.Admin protected-profile reference found",
            1 => "1 Licensing.Admin protected-profile reference",
            > 100 => "More than 100 Licensing.Admin protected-profile references",
            _ => $"{profileCount} Licensing.Admin protected-profile references"
        };
        var state = !reachable ? "Unavailable" : profileCount > 0 ? "Reachable" : "Unconfigured";
        var evidence = reachable
            ? "Public health endpoint responded. Profile files were counted by name only; authenticated scope was not tested."
            : "Public health endpoint did not respond. Existing protected profiles were not opened.";
        WorkspaceConnectionEntry entry = new(
            "licensing:control", "Evotec Control", Provider, "Product services", state, PortalEndpoint, credential,
            ObservedCapabilities(reachable, profileCount), reachable ? DateTimeOffset.UtcNow : null,
            evidence, "Licensing.Core, Licensing.Admin and Licensing.Release");
        return new ConnectionSourceResult([entry], new WorkspaceConnectionSourceState(Provider, state, 1, evidence));
    }

    private static string DefaultProfileRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "Licensing", "profiles");
    }

    private static IReadOnlyList<string> ObservedCapabilities(bool reachable, int profileCount)
    {
        var capabilities = new List<string>(2);
        if (reachable) capabilities.Add("Public health");
        if (profileCount > 0) capabilities.Add("Protected profile reference");
        return capabilities;
    }
}
