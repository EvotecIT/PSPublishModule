namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record PortfolioInteractionResult(
    bool Handled,
    string? SelectedRepositoryFamilyKey,
    string? SelectedRepositoryRootPath,
    bool ShouldScheduleSave)
{
    public static PortfolioInteractionResult Ignored { get; } = new(false, null, null, false);
}
