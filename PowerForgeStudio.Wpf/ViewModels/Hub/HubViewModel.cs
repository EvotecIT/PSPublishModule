using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class HubViewModel : ViewModelBase, IDisposable
{
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ProjectGitService _gitService;
    private readonly GitHubProjectService _gitHubService;
    private readonly ProjectBuildService _buildService;
    private readonly ProjectCacheDatabase _cacheDb;

    private string _searchText = string.Empty;
    private ProjectCategory? _selectedCategory;
    private ProjectListItemViewModel? _selectedProject;
    private ProjectWorkspaceViewModel? _activeWorkspace;
    private ProjectActionsViewModel? _activeActions;
    private HubDashboardViewModel _dashboard;
    private string _statusText = "Ready";
    private bool _isScanning;
    private int _primaryProjectCount;
    private int _worktreeCount;

    private readonly List<ProjectListItemViewModel> _allProjects = [];
    private readonly DispatcherTimer _gitRefreshTimer;
    private readonly DispatcherTimer _githubRefreshTimer;

    public HubViewModel()
        : this(
            new ProjectDiscoveryService(),
            new ProjectGitService(),
            new GitHubProjectService(),
            new ProjectBuildService(),
            new ProjectCacheDatabase(ProjectCacheDatabase.GetDefaultDatabasePath()))
    {
    }

    public HubViewModel(
        ProjectDiscoveryService discoveryService,
        ProjectGitService gitService,
        GitHubProjectService gitHubService,
        ProjectBuildService buildService,
        ProjectCacheDatabase cacheDb)
    {
        _discoveryService = discoveryService;
        _gitService = gitService;
        _gitHubService = gitHubService;
        _buildService = buildService;
        _cacheDb = cacheDb;
        _dashboard = new HubDashboardViewModel(HubDashboardSnapshot.Empty);

        FilteredProjects = new BatchObservableCollection<ProjectListItemViewModel>();

        GroupedView = CollectionViewSource.GetDefaultView(FilteredProjects);
        GroupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectListItemViewModel.CategoryDisplay)));

        ScanWorkspaceCommand = new AsyncDelegateCommand(ScanWorkspaceAsync, () => !_isScanning);
        RefreshSelectedCommand = new AsyncDelegateCommand(RefreshSelectedProjectAsync, () => _selectedProject is not null);

        // Background refresh: git status every 60s
        _gitRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _gitRefreshTimer.Tick += async (_, _) => await RefreshSelectedGitStatusAsync().ConfigureAwait(true);

        // Background refresh: GitHub data every 10 minutes
        _githubRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _githubRefreshTimer.Tick += async (_, _) => await RefreshGitHubCountsAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _gitRefreshTimer.Stop();
        _githubRefreshTimer.Stop();
        _gitHubService.Dispose();
    }

    public BatchObservableCollection<ProjectListItemViewModel> FilteredProjects { get; }

    public ICollectionView GroupedView { get; }

    public ProjectListItemViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            var previous = _selectedProject;
            if (SetProperty(ref _selectedProject, value))
            {
                if (previous is not null)
                {
                    previous.IsSelected = false;
                }

                if (value is not null)
                {
                    value.IsSelected = true;
                    _ = OnProjectSelectedAsync(value);
                }
                else
                {
                    ActiveWorkspace = null;
                    ActiveActions = null;
                }

                RaisePropertyChanged(nameof(HasSelectedProject));
            }
        }
    }

    public bool HasSelectedProject => _selectedProject is not null;

    public ProjectWorkspaceViewModel? ActiveWorkspace
    {
        get => _activeWorkspace;
        private set => SetProperty(ref _activeWorkspace, value);
    }

    public ProjectActionsViewModel? ActiveActions
    {
        get => _activeActions;
        private set => SetProperty(ref _activeActions, value);
    }

    public HubDashboardViewModel Dashboard
    {
        get => _dashboard;
        private set => SetProperty(ref _dashboard, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public ProjectCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ScanWorkspaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WorkspaceRoot { get; set; } = @"C:\Support\GitHub";

    public AsyncDelegateCommand ScanWorkspaceCommand { get; }

    public AsyncDelegateCommand RefreshSelectedCommand { get; }

    public async Task InitializeAsync()
    {
        // Initialize cache database
        try
        {
            await _cacheDb.InitializeAsync().ConfigureAwait(true);
        }
        catch
        {
            // Cache init failure is non-fatal
        }

        // Try loading from cache first for instant startup
        var cached = await TryLoadFromCacheAsync().ConfigureAwait(true);
        if (cached)
        {
            StatusText = $"{_primaryProjectCount} projects (cached)";
            // Scan in background for fresh data
            _ = ScanWorkspaceAsync();
        }
        else
        {
            await ScanWorkspaceAsync().ConfigureAwait(true);
        }

        // Auto-select last project
        AutoSelectLastProject();

        // Start background timers
        _gitRefreshTimer.Start();
        _githubRefreshTimer.Start();

        // Kick off initial GitHub fetch and git status scan
        _ = RefreshGitHubCountsAsync();
        _ = RefreshDirtyRepoCountAsync();
    }

    private void AutoSelectLastProject()
    {
        var lastProjectName = Themes.WindowStateService.GetLastProjectName();
        if (string.IsNullOrWhiteSpace(lastProjectName)) return;

        var match = FilteredProjects.FirstOrDefault(p =>
            string.Equals(p.Name, lastProjectName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            SelectedProject = match;
        }
    }

    private async Task<bool> TryLoadFromCacheAsync()
    {
        try
        {
            var lastScan = await _cacheDb.GetLastScanTimeAsync().ConfigureAwait(true);
            if (!lastScan.HasValue || DateTimeOffset.UtcNow - lastScan.Value > TimeSpan.FromHours(24))
            {
                return false;
            }

            var entries = await _cacheDb.LoadProjectEntriesAsync().ConfigureAwait(true);
            if (entries.Count == 0)
            {
                return false;
            }

            PopulateFromEntries(entries);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ScanWorkspaceAsync()
    {
        IsScanning = true;
        StatusText = "Scanning workspace...";

        try
        {
            var progress = new Progress<int>(count => StatusText = $"Scanning... {count} directories");
            var entries = await _discoveryService.ScanWorkspaceAsync(WorkspaceRoot, progress).ConfigureAwait(true);

            PopulateFromEntries(entries);
            StatusText = $"{_primaryProjectCount} projects, {_worktreeCount} worktrees";

            // Save to cache in background
            try
            {
                await _cacheDb.SaveProjectEntriesAsync(entries).ConfigureAwait(true);
            }
            catch
            {
                // Cache save failure is non-fatal
            }
        }
        catch (Exception exception)
        {
            StatusText = $"Scan failed: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void PopulateFromEntries(IReadOnlyList<ProjectEntry> entries)
    {
        var (primaryProjects, worktreeMap) = GroupWorktrees(entries);

        _allProjects.Clear();
        var totalWorktrees = 0;
        foreach (var vm in primaryProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (worktreeMap.TryGetValue(vm.Name, out var worktrees))
            {
                foreach (var wt in worktrees.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase))
                {
                    vm.Worktrees.Add(wt);
                }

                totalWorktrees += worktrees.Count;
            }

            _allProjects.Add(vm);
        }

        _primaryProjectCount = _allProjects.Count;
        _worktreeCount = totalWorktrees;
        ApplyFilter();

        var primaryEntries = _allProjects.Select(p => p.Entry).ToList();
        var dashboard = new HubDashboardSnapshot(
            TotalProjects: primaryEntries.Count,
            ManagedProjects: primaryEntries.Count(e => e.IsReleaseManaged),
            DirtyRepos: 0,
            TotalOpenPrs: 0,
            TotalOpenIssues: 0,
            FailingBuilds: 0,
            ProjectsAheadOfUpstream: 0,
            RecentlyActive: primaryEntries
                .Where(e => e.LastCommitUtc.HasValue)
                .OrderByDescending(e => e.LastCommitUtc!.Value)
                .Take(10)
                .ToList(),
            NeedingAttention: [],
            StaleProjects: primaryEntries
                .Where(e => e.LastCommitUtc.HasValue
                    && e.LastCommitUtc.Value < DateTimeOffset.UtcNow.AddDays(-90))
                .OrderBy(e => e.LastCommitUtc!.Value)
                .Take(20)
                .ToList());

        Dashboard = new HubDashboardViewModel(dashboard);
    }

    private async Task RefreshGitHubCountsAsync()
    {
        if (_gitHubService.IsRateLimited)
        {
            return;
        }

        var totalPrs = 0;
        var totalIssues = 0;

        foreach (var project in _allProjects)
        {
            if (project.GitHubSlug is null)
            {
                continue;
            }

            try
            {
                // Check cache first
                var cached = await _cacheDb.LoadGitHubCacheAsync(
                    project.GitHubSlug, TimeSpan.FromMinutes(10)).ConfigureAwait(true);

                int prCount;
                int issueCount;

                if (cached is not null)
                {
                    prCount = cached.OpenPrCount ?? 0;
                    issueCount = cached.OpenIssueCount ?? 0;
                }
                else
                {
                    prCount = await _gitHubService.GetOpenPullRequestCountAsync(project.GitHubSlug).ConfigureAwait(true);
                    issueCount = await _gitHubService.GetOpenIssueCountAsync(project.GitHubSlug).ConfigureAwait(true);

                    // Save to cache
                    try
                    {
                        await _cacheDb.SaveGitHubCacheAsync(
                            project.GitHubSlug, prCount, issueCount, null, null).ConfigureAwait(true);
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }

                project.OpenPrCount = prCount;
                project.OpenIssueCount = issueCount;
                totalPrs += prCount;
                totalIssues += issueCount;

                if (_gitHubService.IsRateLimited)
                {
                    StatusText = "GitHub rate limit reached, using cached data.";
                    break;
                }
            }
            catch
            {
                // Skip this project
            }
        }

        // Update dashboard counts
        var currentSnapshot = Dashboard.Snapshot;
        Dashboard = new HubDashboardViewModel(currentSnapshot with
        {
            TotalOpenPrs = totalPrs,
            TotalOpenIssues = totalIssues
        });
    }

    private async Task RefreshDirtyRepoCountAsync()
    {
        var dirtyCount = 0;

        foreach (var project in _allProjects)
        {
            try
            {
                var status = await _gitService.GetStatusAsync(project.Entry.RootPath).ConfigureAwait(true);
                project.BranchName = status.BranchName;
                project.IsDirty = status.IsDirty;

                if (status.IsDirty)
                {
                    dirtyCount++;
                }
            }
            catch
            {
                // Skip
            }
        }

        var current = Dashboard.Snapshot;
        Dashboard = new HubDashboardViewModel(current with { DirtyRepos = dirtyCount });
    }

    private async Task RefreshSelectedGitStatusAsync()
    {
        if (_selectedProject is null || ActiveActions is null)
        {
            return;
        }

        try
        {
            var gitStatus = await _gitService.GetStatusAsync(_selectedProject.Entry.RootPath).ConfigureAwait(true);
            _selectedProject.BranchName = gitStatus.BranchName;
            _selectedProject.IsDirty = gitStatus.IsDirty;
        }
        catch
        {
            // Non-fatal
        }
    }

    private static (List<ProjectListItemViewModel> PrimaryProjects, Dictionary<string, List<ProjectListItemViewModel>> WorktreeMap) GroupWorktrees(
        IReadOnlyList<ProjectEntry> entries)
    {
        var primaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Kind != ProjectKind.Worktree && !IsLikelyWorktree(entry.Name))
            {
                primaryNames.Add(entry.Name);
            }
        }

        var primaryProjects = new List<ProjectListItemViewModel>();
        var worktreeMap = new Dictionary<string, List<ProjectListItemViewModel>>(StringComparer.OrdinalIgnoreCase);
        var unmatchedWorktrees = new List<ProjectEntry>();
        var parentPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.Kind == ProjectKind.Worktree || IsLikelyWorktree(entry.Name))
            {
                var parentName = FindParentProjectName(entry, primaryNames, parentPathMap);
                if (parentName is not null)
                {
                    if (!worktreeMap.TryGetValue(parentName, out var list))
                    {
                        list = [];
                        worktreeMap[parentName] = list;
                    }

                    list.Add(new ProjectListItemViewModel(entry));
                }
                else
                {
                    unmatchedWorktrees.Add(entry);
                }
            }
            else
            {
                primaryProjects.Add(new ProjectListItemViewModel(entry));
            }
        }

        foreach (var entry in unmatchedWorktrees)
        {
            primaryProjects.Add(new ProjectListItemViewModel(entry));
        }

        return (primaryProjects, worktreeMap);
    }

    private static bool IsLikelyWorktree(string name)
        => name.StartsWith(".wt-", StringComparison.OrdinalIgnoreCase);

    private static string? FindParentProjectName(ProjectEntry entry, HashSet<string> primaryNames, Dictionary<string, string> parentPathMap)
    {
        // Method 1: Read .git file to find actual parent repo path
        var parentFromGitFile = ResolveParentFromGitFile(entry.RootPath);
        if (parentFromGitFile is not null && primaryNames.Contains(parentFromGitFile))
        {
            return parentFromGitFile;
        }

        // Also check via the parentPathMap (maps worktree root to resolved parent name)
        if (parentFromGitFile is not null)
        {
            // The .git file pointed to a path; try to match the parent dir name
            foreach (var primary in primaryNames)
            {
                if (string.Equals(primary, parentFromGitFile, StringComparison.OrdinalIgnoreCase))
                {
                    return primary;
                }
            }
        }

        var worktreeName = entry.Name;

        // Method 2: Dot-prefix worktrees (.wt-ix-xxx -> IntelligenceX via abbreviation)
        if (worktreeName.StartsWith(".wt-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = worktreeName[4..];
            foreach (var primary in primaryNames)
            {
                var abbreviation = GetAbbreviation(primary);
                if (abbreviation is not null
                    && suffix.StartsWith(abbreviation + "-", StringComparison.OrdinalIgnoreCase))
                {
                    return primary;
                }
            }
        }

        // Method 3: Name prefix matching (longest match wins)
        string? bestMatch = null;
        var bestLength = 0;

        foreach (var primary in primaryNames)
        {
            if (worktreeName.StartsWith(primary + "-", StringComparison.OrdinalIgnoreCase)
                && primary.Length > bestLength)
            {
                bestMatch = primary;
                bestLength = primary.Length;
            }
        }

        return bestMatch;
    }

    private static string? ResolveParentFromGitFile(string rootPath)
    {
        var gitFilePath = Path.Combine(rootPath, ".git");
        try
        {
            if (!File.Exists(gitFilePath))
            {
                return null;
            }

            var content = File.ReadAllText(gitFilePath).Trim();
            if (!content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Format: "gitdir: C:/Support/GitHub/IntelligenceX/.git/worktrees/IntelligenceX-autonomy-next"
            var gitdir = content["gitdir:".Length..].Trim();

            // Navigate up: .git/worktrees/<name> -> extract parent repo dir name
            // The parent .git is at: <path>/.git/worktrees/<wt-name>
            // So parent repo is at: <path>
            var worktreesIndex = gitdir.Replace('\\', '/').IndexOf("/.git/worktrees/", StringComparison.OrdinalIgnoreCase);
            if (worktreesIndex >= 0)
            {
                var parentRepoPath = gitdir[..worktreesIndex];
                return Path.GetFileName(parentRepoPath);
            }
        }
        catch
        {
            // Ignore read errors
        }

        return null;
    }

    private static string? GetAbbreviation(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        var chars = new List<char>();
        foreach (var ch in projectName)
        {
            if (char.IsUpper(ch))
            {
                chars.Add(char.ToLowerInvariant(ch));
            }
        }

        return chars.Count >= 2 ? new string(chars.ToArray()) : null;
    }

    private void ApplyFilter()
    {
        var filtered = new List<ProjectListItemViewModel>();

        foreach (var project in _allProjects)
        {
            if (_selectedCategory.HasValue && project.Category != _selectedCategory.Value)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var matchesProject = project.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
                var matchesWorktree = project.Worktrees.Any(wt =>
                    wt.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

                if (!matchesProject && !matchesWorktree)
                {
                    continue;
                }
            }

            filtered.Add(project);
        }

        // Single Reset notification instead of N individual Add notifications
        FilteredProjects.ReplaceAll(filtered);
    }

    public async Task OnProjectSelectedAsync(ProjectListItemViewModel project)
    {
        try
        {
            StatusText = $"Loading {project.Name}...";

            var gitStatus = await _gitService.GetStatusAsync(project.Entry.RootPath).ConfigureAwait(true);
            project.BranchName = gitStatus.BranchName;
            project.IsDirty = gitStatus.IsDirty;

            // Dispose previous workspace resources (terminal, file explorer)
            if (ActiveWorkspace is not null)
            {
                await ActiveWorkspace.DisposeAsync().ConfigureAwait(true);
            }

            ActiveWorkspace = new ProjectWorkspaceViewModel(project.Entry, _gitHubService, _buildService);
            ActiveActions = new ProjectActionsViewModel(project.Entry, gitStatus, _gitService);

            StatusText = $"{project.Name} loaded.";
        }
        catch (Exception exception)
        {
            StatusText = $"Failed to load {project.Name}: {exception.Message}";
        }
    }

    private async Task RefreshSelectedProjectAsync()
    {
        if (_selectedProject is not null)
        {
            await OnProjectSelectedAsync(_selectedProject).ConfigureAwait(true);
        }
    }
}
