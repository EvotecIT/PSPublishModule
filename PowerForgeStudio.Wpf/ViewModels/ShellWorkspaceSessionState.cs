using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ShellWorkspaceSessionState
{
    private static readonly RepositoryPortfolioSummary EmptySummary = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public ShellWorkspaceSessionState(PSPublishModuleResolution buildEngineResolution)
    {
        BuildEngineResolution = buildEngineResolution;
    }

    public ReleaseQueueSession? ActiveQueueSession { get; private set; }

    public RepositoryPortfolioSummary PortfolioSummary { get; private set; } = EmptySummary;

    public IReadOnlyList<RepositoryPortfolioItem> PortfolioSnapshot { get; private set; } = [];

    public IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> GitQuickActionReceiptLookup { get; private set; }
        = new Dictionary<string, RepositoryGitQuickActionReceipt>(StringComparer.OrdinalIgnoreCase);

    public PSPublishModuleResolution BuildEngineResolution { get; private set; }

    public ShellWorkspaceStationSnapshots StationSnapshots { get; private set; } = ShellWorkspaceStationSnapshots.Empty;

    public RepositoryPortfolioItem? SelectedRepository { get; private set; }

    public string? SelectedRepositoryRootPath { get; private set; }

    public string? SelectedRepositoryFamilyKey { get; private set; }

    public void ApplyWorkspaceSnapshot(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PortfolioSummary = snapshot.Summary;
        PortfolioSnapshot = snapshot.PortfolioItems;
        GitQuickActionReceiptLookup = CreateGitQuickActionReceiptLookup(snapshot.GitQuickActionReceipts);
        BuildEngineResolution = snapshot.BuildEngineResolution;
        ActiveQueueSession = snapshot.QueueSession;
        StationSnapshots = ShellWorkspaceStationSnapshots.FromWorkspaceSnapshot(snapshot);
    }

    public void ResetWorkspaceContext()
    {
        PortfolioSummary = EmptySummary;
        PortfolioSnapshot = [];
        GitQuickActionReceiptLookup = new Dictionary<string, RepositoryGitQuickActionReceipt>(StringComparer.OrdinalIgnoreCase);
        ActiveQueueSession = null;
        StationSnapshots = ShellWorkspaceStationSnapshots.Empty;
        SelectedRepository = null;
        SelectedRepositoryRootPath = null;
        SelectedRepositoryFamilyKey = null;
    }

    public void RestoreSavedPortfolioView(RepositoryPortfolioViewState? viewState)
    {
        SelectedRepositoryFamilyKey = viewState?.FamilyKey;
    }

    public void SetSelectedRepositoryFamilyKey(string? familyKey)
    {
        SelectedRepositoryFamilyKey = string.IsNullOrWhiteSpace(familyKey)
            ? null
            : familyKey;
    }

    public void ApplyQueueCommandResult(
        ReleaseQueueCommandResult result,
        ReleaseStationProjectionService stationProjectionService)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stationProjectionService);

        if (result.QueueSession is null)
        {
            return;
        }

        ActiveQueueSession = result.QueueSession;
        StationSnapshots = ShellWorkspaceStationSnapshots.FromQueueCommandResult(result, stationProjectionService);
    }

    public void ApplyGitQuickActionReceipt(RepositoryGitQuickActionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        GitQuickActionReceiptLookup = new Dictionary<string, RepositoryGitQuickActionReceipt>(GitQuickActionReceiptLookup, StringComparer.OrdinalIgnoreCase) {
            [receipt.RootPath] = receipt
        };
    }

    public void ApplyProjectionResult(ShellWorkspaceProjectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        PortfolioSnapshot = result.AnnotatedPortfolioItems;
        SelectedRepositoryFamilyKey = result.SelectedRepositoryFamilyKey;
        SetSelectedRepository(result.SelectedRepository);
    }

    public void ApplyPortfolioInteraction(PortfolioInteractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Handled)
        {
            return;
        }

        SelectedRepositoryFamilyKey = result.SelectedRepositoryFamilyKey;
        if (!string.IsNullOrWhiteSpace(result.SelectedRepositoryRootPath))
        {
            SelectedRepositoryRootPath = result.SelectedRepositoryRootPath;
        }
    }

    public void SetSelectedRepository(RepositoryPortfolioItem? repository)
    {
        SelectedRepository = repository;
        SelectedRepositoryRootPath = repository?.RootPath;
    }

    private static IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> CreateGitQuickActionReceiptLookup(
        IReadOnlyList<RepositoryGitQuickActionReceipt> receipts)
        => receipts
            .GroupBy(receipt => receipt.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(receipt => receipt.ExecutedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
}
