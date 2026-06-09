using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class ProjectActionsViewModel : ViewModelBase
{
    private readonly ProjectEntry _entry;
    private readonly ProjectGitService _gitService;
    private ProjectGitStatus _gitStatus;
    private GitFileChange? _selectedFile;
    private string _diffContent = string.Empty;
    private string _commitMessage = string.Empty;
    private string? _newBranchName;
    private bool _isCommitting;

    public ProjectActionsViewModel(
        ProjectEntry entry,
        ProjectGitStatus gitStatus,
        ProjectGitService gitService)
    {
        _entry = entry;
        _gitStatus = gitStatus;
        _gitService = gitService;

        StagedChanges = new ObservableCollection<GitFileChange>(gitStatus.StagedChanges);
        UnstagedChanges = new ObservableCollection<GitFileChange>(gitStatus.UnstagedChanges.Concat(gitStatus.UntrackedFiles));
        Branches = new ObservableCollection<string>(gitStatus.Branches);
        Worktrees = new ObservableCollection<GitWorktreeEntry>(gitStatus.Worktrees);

        StageFileCommand = new AsyncDelegateCommand(StageSelectedFileAsync);
        UnstageFileCommand = new AsyncDelegateCommand(UnstageSelectedFileAsync);
        StageAllCommand = new AsyncDelegateCommand(StageAllAsync);
        CommitCommand = new AsyncDelegateCommand(CommitAsync, () => !_isCommitting && !string.IsNullOrWhiteSpace(_commitMessage));
        CreateBranchCommand = new AsyncDelegateCommand(CreateBranchAsync, () => !string.IsNullOrWhiteSpace(_newBranchName));
        SwitchBranchCommand = new AsyncDelegateCommand(SwitchBranchAsync);
        RefreshStatusCommand = new AsyncDelegateCommand(RefreshStatusAsync);
    }

    public string BranchName => _gitStatus.BranchDisplay;

    public string AheadBehindDisplay => _gitStatus.AheadBehindDisplay;

    public string StatusSummary => _gitStatus.StatusSummary;

    public bool IsDirty => _gitStatus.IsDirty;

    public ObservableCollection<GitFileChange> StagedChanges { get; }

    public ObservableCollection<GitFileChange> UnstagedChanges { get; }

    public ObservableCollection<string> Branches { get; }

    public ObservableCollection<GitWorktreeEntry> Worktrees { get; }

    public GitFileChange? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value) && value is not null)
            {
                _ = LoadDiffAsync(value);
            }
        }
    }

    public string DiffContent
    {
        get => _diffContent;
        private set
        {
            if (SetProperty(ref _diffContent, value))
            {
                ParseDiffLines(value);
            }
        }
    }

    public ObservableCollection<DiffLine> DiffLines { get; } = [];

    private void ParseDiffLines(string diff)
    {
        DiffLines.Clear();
        if (string.IsNullOrWhiteSpace(diff) || diff == "(No diff available)")
        {
            return;
        }

        foreach (var line in diff.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            DiffLines.Add(DiffLine.Parse(line));
        }
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            if (SetProperty(ref _commitMessage, value))
            {
                CommitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? NewBranchName
    {
        get => _newBranchName;
        set
        {
            if (SetProperty(ref _newBranchName, value))
            {
                CreateBranchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedBranch { get; set; }

    public AsyncDelegateCommand StageFileCommand { get; }
    public AsyncDelegateCommand UnstageFileCommand { get; }
    public AsyncDelegateCommand StageAllCommand { get; }
    public AsyncDelegateCommand CommitCommand { get; }
    public AsyncDelegateCommand CreateBranchCommand { get; }
    public AsyncDelegateCommand SwitchBranchCommand { get; }
    public AsyncDelegateCommand RefreshStatusCommand { get; }

    private async Task LoadDiffAsync(GitFileChange file)
    {
        var isStagedFile = StagedChanges.Contains(file);
        var diff = await _gitService.GetDiffAsync(_entry.RootPath, file.Path, staged: isStagedFile).ConfigureAwait(true);
        DiffContent = string.IsNullOrWhiteSpace(diff) ? "(No diff available)" : diff;
    }

    private async Task StageSelectedFileAsync()
    {
        if (_selectedFile is null)
        {
            return;
        }

        await _gitService.StageFileAsync(_entry.RootPath, _selectedFile.Path).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task UnstageSelectedFileAsync()
    {
        if (_selectedFile is null)
        {
            return;
        }

        await _gitService.UnstageFileAsync(_entry.RootPath, _selectedFile.Path).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task StageAllAsync()
    {
        await _gitService.StageAllAsync(_entry.RootPath).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(_commitMessage))
        {
            return;
        }

        _isCommitting = true;
        CommitCommand.RaiseCanExecuteChanged();

        try
        {
            await _gitService.CommitAsync(_entry.RootPath, _commitMessage).ConfigureAwait(true);
            CommitMessage = string.Empty;
            await RefreshStatusAsync().ConfigureAwait(true);
        }
        finally
        {
            _isCommitting = false;
            CommitCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(_newBranchName))
        {
            return;
        }

        await _gitService.CreateBranchAsync(_entry.RootPath, _newBranchName).ConfigureAwait(true);
        NewBranchName = null;
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task SwitchBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBranch))
        {
            return;
        }

        await _gitService.SwitchBranchAsync(_entry.RootPath, SelectedBranch).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task RefreshStatusAsync()
    {
        _gitStatus = await _gitService.GetStatusAsync(_entry.RootPath).ConfigureAwait(true);

        StagedChanges.Clear();
        foreach (var change in _gitStatus.StagedChanges)
        {
            StagedChanges.Add(change);
        }

        UnstagedChanges.Clear();
        foreach (var change in _gitStatus.UnstagedChanges.Concat(_gitStatus.UntrackedFiles))
        {
            UnstagedChanges.Add(change);
        }

        Branches.Clear();
        foreach (var branch in _gitStatus.Branches)
        {
            Branches.Add(branch);
        }

        Worktrees.Clear();
        foreach (var worktree in _gitStatus.Worktrees)
        {
            Worktrees.Add(worktree);
        }

        RaisePropertyChanged(nameof(BranchName));
        RaisePropertyChanged(nameof(AheadBehindDisplay));
        RaisePropertyChanged(nameof(StatusSummary));
        RaisePropertyChanged(nameof(IsDirty));
    }
}
