using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Persists machine-local Studio preferences in the existing workspace catalog.</summary>
public interface IWorkspacePreferenceService
{
    string ConfigurationPath { get; }

    WorkspaceRootCatalog SavePreferences(
        WorkspaceStudioPreferences preferences,
        string fallbackWorkspaceRoot,
        string? activeProfileId = null);
}
