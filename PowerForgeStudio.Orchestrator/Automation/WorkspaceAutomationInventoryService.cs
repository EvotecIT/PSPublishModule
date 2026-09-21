using PowerForge;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Automation;

/// <summary>Combines read-only schedule evidence while preserving each provider boundary.</summary>
public sealed class WorkspaceAutomationInventoryService : IWorkspaceAutomationInventoryService, IDisposable
{
    private readonly WindowsTaskAutomationSource _windows;
    private readonly GitHubWorkflowAutomationSource _gitHub;
    private readonly GitHubWorkflowRuntimeSource _gitHubRuntime;
    private readonly bool _ownsRuntime;

    public WorkspaceAutomationInventoryService(IProcessRunner? processRunner = null, IWorkspaceRepositorySource? repositories = null)
        : this(processRunner, repositories, null)
    {
    }

    internal WorkspaceAutomationInventoryService(IProcessRunner? processRunner, IWorkspaceRepositorySource? repositories,
        GitHubWorkflowRuntimeSource? gitHubRuntime)
    {
        _windows = new WindowsTaskAutomationSource(processRunner);
        _gitHub = new GitHubWorkflowAutomationSource(repositories);
        _gitHubRuntime = gitHubRuntime ?? new GitHubWorkflowRuntimeSource();
        _ownsRuntime = gitHubRuntime is null;
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
            var local = await _gitHub.ReadAsync(root, token).ConfigureAwait(false);
            return await _gitHubRuntime.EnrichAsync(local, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new([], new("GitHub Actions", "Unavailable", 0,
                "GitHub workflow definitions or remote runtime evidence could not be read."));
        }
    }

    public void Dispose()
    {
        if (_ownsRuntime) _gitHubRuntime.Dispose();
    }
}
