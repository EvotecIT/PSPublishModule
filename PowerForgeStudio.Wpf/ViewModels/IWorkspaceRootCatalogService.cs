namespace PowerForgeStudio.Wpf.ViewModels;

public interface IWorkspaceRootCatalogService
{
    WorkspaceRootCatalog Load(string fallbackWorkspaceRoot);

    WorkspaceRootCatalog SaveActive(string workspaceRoot, string? activeProfileId = null);

    WorkspaceRootCatalog SaveProfile(WorkspaceProfile profile, string? activeProfileId = null);

    WorkspaceRootCatalog DeleteProfile(string profileId, string fallbackWorkspaceRoot, string? activeProfileId = null);

    WorkspaceRootCatalog SaveTemplate(WorkspaceProfileTemplate template, string fallbackWorkspaceRoot, string? activeProfileId = null);

    WorkspaceRootCatalog DeleteTemplate(string templateId, string fallbackWorkspaceRoot, string? activeProfileId = null);
}
