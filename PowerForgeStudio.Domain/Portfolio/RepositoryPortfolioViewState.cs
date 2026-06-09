namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryPortfolioViewState(
    string? PresetKey,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText,
    string? FamilyKey,
    DateTimeOffset UpdatedAtUtc);

