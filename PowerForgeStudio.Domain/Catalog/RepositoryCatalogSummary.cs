namespace PowerForgeStudio.Domain.Catalog;

public sealed record RepositoryCatalogSummary(
    int TotalRepositories,
    int ManagedRepositories,
    int ModuleRepositories,
    int LibraryRepositories,
    int WorktreeRepositories);

