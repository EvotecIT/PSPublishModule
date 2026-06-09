using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class PortfolioInteractionService : IPortfolioInteractionService
{
    public PortfolioInteractionResult ApplyPreset(PortfolioOverviewViewModel portfolioOverview, PortfolioQuickPreset? preset)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);

        if (preset is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        portfolioOverview.SelectedPreset = preset;
        portfolioOverview.SelectedFocus = ResolveFocus(portfolioOverview, preset.FocusMode);
        portfolioOverview.SearchText = preset.SearchText;
        portfolioOverview.ViewMemory = $"Preset applied: {preset.DisplayName}.";
        return new PortfolioInteractionResult(true, null, null, false);
    }

    public PortfolioInteractionResult ApplyDashboardCard(
        PortfolioOverviewViewModel portfolioOverview,
        PortfolioDashboardCard? card,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePortfolioPreset)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);
        ArgumentNullException.ThrowIfNull(resolvePortfolioPreset);

        if (card is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        portfolioOverview.SelectedPreset = resolvePortfolioPreset(card.PresetKey, card.FocusMode, card.SearchText);
        portfolioOverview.SelectedFocus = ResolveFocus(portfolioOverview, card.FocusMode);
        portfolioOverview.SearchText = card.SearchText;
        portfolioOverview.ViewMemory = $"Dashboard card applied: {card.Title}.";
        return new PortfolioInteractionResult(true, null, null, false);
    }

    public PortfolioInteractionResult ApplyRepositoryFamily(
        PortfolioOverviewViewModel portfolioOverview,
        RepositoryWorkspaceFamilySnapshot? family)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);

        if (family is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        portfolioOverview.SelectedPreset = null;
        var familyKey = string.IsNullOrWhiteSpace(family.FamilyKey)
            ? null
            : family.FamilyKey;
        portfolioOverview.ViewMemory = string.IsNullOrWhiteSpace(familyKey)
            ? "Repository family filter cleared."
            : $"Repository family applied: {family.DisplayName}.";
        return new PortfolioInteractionResult(true, familyKey, null, true);
    }

    public PortfolioInteractionResult ApplyFamilyLaneItem(
        PortfolioOverviewViewModel portfolioOverview,
        IReadOnlyList<RepositoryPortfolioItem> portfolioSnapshot,
        RepositoryWorkspaceFamilyLaneItem? item)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);
        ArgumentNullException.ThrowIfNull(portfolioSnapshot);

        if (item is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        var selectedRepository = portfolioSnapshot.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase));
        if (selectedRepository is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        portfolioOverview.SelectedPreset = null;
        portfolioOverview.ViewMemory = $"Family lane item applied: {item.LaneDisplay} for {item.RepositoryName}.";
        return new PortfolioInteractionResult(true, selectedRepository.FamilyKey, item.RootPath, true);
    }

    public PortfolioInteractionResult ApplyReleaseInboxItem(
        PortfolioOverviewViewModel portfolioOverview,
        IReadOnlyList<RepositoryPortfolioItem> portfolioSnapshot,
        RepositoryReleaseInboxItem? item,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePortfolioPreset)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);
        ArgumentNullException.ThrowIfNull(portfolioSnapshot);
        ArgumentNullException.ThrowIfNull(resolvePortfolioPreset);

        if (item is null)
        {
            return PortfolioInteractionResult.Ignored;
        }

        portfolioOverview.SelectedPreset = resolvePortfolioPreset(item.PresetKey, item.FocusMode, item.SearchText);
        portfolioOverview.SelectedFocus = ResolveFocus(portfolioOverview, item.FocusMode);
        portfolioOverview.SearchText = item.SearchText;
        portfolioOverview.ViewMemory = $"Release inbox item applied: {item.Badge} for {item.RepositoryName}.";

        var selectedRepository = portfolioSnapshot.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase));
        return new PortfolioInteractionResult(true, selectedRepository?.FamilyKey, item.RootPath, false);
    }

    private static PortfolioFocusOption ResolveFocus(
        PortfolioOverviewViewModel portfolioOverview,
        RepositoryPortfolioFocusMode focusMode)
        => portfolioOverview.FocusModes.FirstOrDefault(option => option.Mode == focusMode)
           ?? portfolioOverview.FocusModes[0];
}
