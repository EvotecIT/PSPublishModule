using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceGitActionCommandResult(
    bool Changed,
    string Message,
    RepositoryGitActionCatalog Catalog,
    RepositoryGitQuickAction? SelectedAction,
    RepositoryGitQuickActionReceipt? Receipt);
