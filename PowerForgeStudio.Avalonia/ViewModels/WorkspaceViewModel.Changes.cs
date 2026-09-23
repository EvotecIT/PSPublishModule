namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    /// <summary>Rereads the visible working copy after another tool may have changed its Git state.</summary>
    public Task RefreshVisibleChangesAsync()
        => !_disposed && IsChangesPage && HasGitWorkingCopy && !Changes.IsLoading && !Changes.IsMutating
            ? Changes.RefreshAsync()
            : Task.CompletedTask;
}
