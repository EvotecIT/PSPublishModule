namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitQuickActionReceipt(
    string RootPath,
    string ActionTitle,
    RepositoryGitQuickActionKind ActionKind,
    string Payload,
    bool Succeeded,
    string Summary,
    string? OutputTail,
    string? ErrorTail,
    DateTimeOffset ExecutedAtUtc)
{
    public string StatusDisplay => Succeeded ? "Succeeded" : "Failed";
}
