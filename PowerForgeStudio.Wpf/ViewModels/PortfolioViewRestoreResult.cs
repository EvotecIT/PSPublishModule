using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record PortfolioViewRestoreResult(
    PortfolioFocusOption SelectedFocus,
    string SearchText,
    string? FamilyKey,
    PortfolioQuickPreset? SelectedPreset,
    string ViewMemory);
