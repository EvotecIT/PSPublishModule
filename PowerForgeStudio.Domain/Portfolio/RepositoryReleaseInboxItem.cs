namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryReleaseInboxItem(
    string RootPath,
    string RepositoryName,
    string Title,
    string Detail,
    string Badge,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText,
    string? PresetKey,
    int Priority);

