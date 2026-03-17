using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public interface IPortfolioInteractionService
{
    PortfolioInteractionResult ApplyPreset(PortfolioOverviewViewModel portfolioOverview, PortfolioQuickPreset? preset);

    PortfolioInteractionResult ApplyDashboardCard(
        PortfolioOverviewViewModel portfolioOverview,
        PortfolioDashboardCard? card,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePortfolioPreset);

    PortfolioInteractionResult ApplyRepositoryFamily(
        PortfolioOverviewViewModel portfolioOverview,
        RepositoryWorkspaceFamilySnapshot? family);

    PortfolioInteractionResult ApplyFamilyLaneItem(
        PortfolioOverviewViewModel portfolioOverview,
        IReadOnlyList<RepositoryPortfolioItem> portfolioSnapshot,
        RepositoryWorkspaceFamilyLaneItem? item);

    PortfolioInteractionResult ApplyReleaseInboxItem(
        PortfolioOverviewViewModel portfolioOverview,
        IReadOnlyList<RepositoryPortfolioItem> portfolioSnapshot,
        RepositoryReleaseInboxItem? item,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePortfolioPreset);
}
