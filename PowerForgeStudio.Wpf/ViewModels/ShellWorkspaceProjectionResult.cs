using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record ShellWorkspaceProjectionResult(
    IReadOnlyList<RepositoryPortfolioItem> AnnotatedPortfolioItems,
    RepositoryPortfolioItem? SelectedRepository,
    string? SelectedRepositoryFamilyKey);
