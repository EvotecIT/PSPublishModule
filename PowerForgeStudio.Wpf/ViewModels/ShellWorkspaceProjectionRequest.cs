using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record ShellWorkspaceProjectionRequest(
    string WorkspaceRoot,
    RepositoryPortfolioSummary Summary,
    IReadOnlyList<RepositoryPortfolioItem> PortfolioItems,
    ReleaseQueueSession? QueueSession,
    IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> GitQuickActionReceiptLookup,
    PSPublishModuleResolution BuildEngineResolution,
    string? SelectedRepositoryRootPath,
    string? SelectedRepositoryFamilyKey,
    PortfolioOverviewViewModel PortfolioOverview,
    RepositoryFamilyViewModel RepositoryFamily,
    ReleaseSignalsViewModel ReleaseSignals,
    ReleaseStationsViewModel Stations,
    ObservableCollection<RepositoryPortfolioItem> Repositories,
    ShellWorkspaceStationSnapshots StationSnapshots,
    Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> ResolvePortfolioPreset,
    IReadOnlyList<RepositoryReleaseInboxItem>? ReleaseInboxItemsOverride = null);
