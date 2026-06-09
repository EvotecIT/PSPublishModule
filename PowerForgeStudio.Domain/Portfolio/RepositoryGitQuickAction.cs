namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitQuickAction(
    string Title,
    string Summary,
    RepositoryGitQuickActionKind Kind,
    string Payload,
    string ExecuteLabel,
    bool IsPrimary = false,
    RepositoryGitOperationKind? GitOperation = null,
    string? GitOperationArgument = null)
{
    public string KindDisplay => Kind == RepositoryGitQuickActionKind.BrowserUrl ? "Browser" : "Git";
}
