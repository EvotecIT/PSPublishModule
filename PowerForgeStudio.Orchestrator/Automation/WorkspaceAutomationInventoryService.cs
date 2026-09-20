using PowerForge;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Automation;

/// <summary>Combines read-only schedule evidence while preserving each provider boundary.</summary>
public sealed class WorkspaceAutomationInventoryService : IWorkspaceAutomationInventoryService
{
    private readonly WindowsTaskAutomationSource _windows;
    private readonly GitHubWorkflowAutomationSource _gitHub;

    public WorkspaceAutomationInventoryService(IProcessRunner? processRunner = null, IWorkspaceRepositorySource? repositories = null)
    {
        _windows = new WindowsTaskAutomationSource(processRunner);
        _gitHub = new GitHubWorkflowAutomationSource(repositories);
    }

    public async Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var windowsTask = ReadWindowsSafelyAsync(root, cancellationToken);
        var gitHubTask = ReadGitHubSafelyAsync(root, cancellationToken);
        await Task.WhenAll(windowsTask, gitHubTask).ConfigureAwait(false);
        var windows = await windowsTask.ConfigureAwait(false);
        var gitHub = await gitHubTask.ConfigureAwait(false);
        var entries = windows.Entries.Concat(gitHub.Entries)
            .OrderBy(static entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ProjectDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WorkspaceAutomationSourceState codex = new("Codex", "Unavailable", 0,
            "No supported external inventory API is available. Studio does not read or edit private Codex runtime files.");
        return new(DateTimeOffset.UtcNow, entries, [windows.State, gitHub.State, codex]);
    }

    private async Task<AutomationSourceResult> ReadWindowsSafelyAsync(string root, CancellationToken token)
    {
        try
        {
            return await _windows.ReadAsync(root, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new([], new("Windows Task Scheduler", "Unavailable", 0,
                "Windows task evidence could not be read."));
        }
    }

    private async Task<AutomationSourceResult> ReadGitHubSafelyAsync(string root, CancellationToken token)
    {
        try
        {
            return await _gitHub.ReadAsync(root, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new([], new("GitHub Actions", "Unavailable", 0,
                "Local GitHub workflow definitions could not be read."));
        }
    }
}
