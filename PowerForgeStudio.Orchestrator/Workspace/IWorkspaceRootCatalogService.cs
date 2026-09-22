using PowerForgeStudio.Domain.Workspace;
namespace PowerForgeStudio.Orchestrator.Workspace;

public interface IWorkspaceRootCatalogService
{
    WorkspaceRootCatalog Load(string fallbackWorkspaceRoot);

    WorkspaceRootCatalog SaveActive(string workspaceRoot, string? activeProfileId = null);

    /// <summary>Removes a non-active, unreferenced root from the recent list without touching its directory or explorer state.</summary>
    WorkspaceRootCatalog ForgetRecentRoot(string workspaceRoot, string fallbackWorkspaceRoot);

    WorkspaceRootCatalog SaveProfile(WorkspaceProfile profile, string? activeProfileId = null);

    WorkspaceRootCatalog DeleteProfile(string profileId, string fallbackWorkspaceRoot, string? activeProfileId = null);

    WorkspaceRootCatalog SaveTemplate(WorkspaceProfileTemplate template, string fallbackWorkspaceRoot, string? activeProfileId = null);

    WorkspaceRootCatalog DeleteTemplate(string templateId, string fallbackWorkspaceRoot, string? activeProfileId = null);
}
