using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Domain.Workspace;

public sealed record RepositoryGitActionCatalog(
    string RepositoryName,
    string RootPath,
    string FamilyDisplayName,
    IReadOnlyList<RepositoryGitQuickAction> Actions,
    RepositoryGitQuickActionReceipt? LatestReceipt);
