using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Projects;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ProjectOverviewViewModel Overview { get; private set; } = null!;
    [ObservableProperty] private bool _isOverviewPage;
    public bool IsProjectRoute => IsOverviewPage || IsFilesPage || IsChangesPage || IsHistoryPage
        || IsBuildPage || IsGitHubPage || IsReleasePage;

    partial void OnIsOverviewPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(ShowGenericProjectContext));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowOverviewAsync()
    {
        if (!ActivateOverview()) return;
        await RefreshOverviewAsync();
    }

    private async Task ShowOverviewFromSelectionAsync()
    {
        if (!ActivateOverview()) return;
        await Overview.RefreshAsync();
    }

    private bool ActivateOverview()
    {
        if (KeepReleaseVisible()) return false;
        IsSettingsPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsHistoryPage = false;
        IsOverviewPage = true;
        return true;
    }

    [RelayCommand]
    private async Task RefreshOverviewAsync()
    {
        var root = ActiveWorkingCopyRoot;
        var selectedProject = _projectNodes.Values.FirstOrDefault(project => project.IsContextProject);
        var catalogEntry = selectedProject is null
            ? null
            : _catalog.FirstOrDefault(entry => SamePath(entry.RootPath, selectedProject.Path));
        if (catalogEntry is null || string.IsNullOrWhiteSpace(root))
        {
            await Overview.RefreshAsync();
            return;
        }

        try
        {
            var git = await _git.GetStatusAsync(root, _lifetime.Token);
            if (!IsActiveOverviewRoot(root)) return;
            Branch = git.IsGitRepository ? git.BranchDisplay : "Local project";
            GitSummary = git.IsGitRepository ? git.StatusSummary : "No Git working copy";
            UpdateGitDecorations(root, git);
            Overview.SetProject(catalogEntry, root, git);
            await Overview.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            if (IsActiveOverviewRoot(root)) Overview.Status = "Overview refresh was cancelled.";
        }
        catch (Exception)
        {
            if (!IsActiveOverviewRoot(root)) return;
            Overview.Status = "Could not refresh Git state for this working copy.";
            Overview.Output = "Git status could not be refreshed. Confirm that the working copy still exists and Git is available, then retry.";
        }
    }

    private bool IsActiveOverviewRoot(string root)
        => !_disposed && !string.IsNullOrWhiteSpace(ActiveWorkingCopyRoot) && SamePath(root, ActiveWorkingCopyRoot);

    private async Task OpenOverviewEntryPointAsync(ProjectOverviewItem item)
    {
        if (KeepReleaseVisible()) throw new InvalidOperationException("Finish the active release before opening another project file.");
        var root = Overview.WorkingCopyRoot;
        if (string.IsNullOrWhiteSpace(root) || !SamePath(root, ActiveWorkingCopyRoot) ||
            item.SourcePath is not { } sourcePath || !Path.IsPathFullyQualified(sourcePath))
            throw new InvalidOperationException("The selected working copy changed. Refresh Project Overview.");

        var source = Path.GetFullPath(sourcePath);
        if (SamePath(source, root) || !WorkspacePathContainment.ContainsOrEquals(root, source) || !File.Exists(source))
            throw new InvalidOperationException("The entrypoint is no longer a file inside the selected working copy. Refresh Project Overview.");

        for (var current = source; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Entrypoints reached through symbolic links or junctions cannot open in Studio.");
            if (SamePath(current, root)) break;
        }

        await SelectAsync(new ExplorerNode(Path.GetFileName(source), source, "file", root));
    }
}
