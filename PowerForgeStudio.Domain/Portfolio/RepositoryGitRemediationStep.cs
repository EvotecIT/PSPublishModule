namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitRemediationStep(
    string Title,
    string Summary,
    string CommandText,
    bool IsPrimary = false,
    RepositoryGitOperationKind? GitOperation = null,
    string? GitOperationArgument = null);
