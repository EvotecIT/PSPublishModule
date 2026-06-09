namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitQuickActionExecutionResult(
    bool Succeeded,
    string Summary,
    string? OutputTail = null,
    string? ErrorTail = null);
