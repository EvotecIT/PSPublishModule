using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class ProjectListItemViewModel : ViewModelBase
{
    private int _openPrCount;
    private int _openIssueCount;
    private bool _isDirty;
    private string? _branchName;
    private bool _isSelected;
    private bool _isExpanded;

    public ProjectListItemViewModel(ProjectEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Worktrees = [];
    }

    public ProjectEntry Entry { get; }

    public string Name => Entry.Name;

    public string CategoryDisplay => Entry.CategoryDisplay;

    public ProjectCategory Category => Entry.Category;

    public ProjectKind Kind => Entry.Kind;

    public string? GitHubSlug => Entry.GitHubSlug;

    public bool IsReleaseManaged => Entry.IsReleaseManaged;

    public bool IsWorktree => Entry.Kind == ProjectKind.Worktree;

    public ObservableCollection<ProjectListItemViewModel> Worktrees { get; }

    public bool HasWorktrees => Worktrees.Count > 0;

    public string WorktreeCountDisplay => Worktrees.Count > 0 ? $"{Worktrees.Count} worktree(s)" : string.Empty;

    public string WorktreeTooltip => Worktrees.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, Worktrees.Select(worktree => worktree.Name));

    public string? LastActivity => Entry.LastCommitUtc.HasValue
        ? Domain.Hub.RelativeTimeFormatter.Format(Entry.LastCommitUtc.Value)
        : null;

    public int OpenPrCount
    {
        get => _openPrCount;
        set
        {
            if (SetProperty(ref _openPrCount, value))
            {
                RaisePropertyChanged(nameof(HasOpenPrs));
            }
        }
    }

    public bool HasOpenPrs => _openPrCount > 0;

    public int OpenIssueCount
    {
        get => _openIssueCount;
        set => SetProperty(ref _openIssueCount, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public string? BranchName
    {
        get => _branchName;
        set => SetProperty(ref _branchName, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
