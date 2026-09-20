namespace PowerForgeStudio.Wpf.ViewModels;

public sealed partial class ShellViewModel
{
    private bool TrySaveWorkspaceCatalog(Func<WorkspaceRootCatalog> save)
    {
        try
        {
            ApplyWorkspaceRootCatalog(save());
            return true;
        }
        catch (Exception exception)
        {
            StatusText = "Could not save workspace settings: " + exception.Message;
            return false;
        }
    }
}
