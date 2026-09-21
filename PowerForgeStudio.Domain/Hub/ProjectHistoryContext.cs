namespace PowerForgeStudio.Domain.Hub;

public sealed record ProjectHistoryContext(bool IsGitRepository, string Branch)
{
    public static ProjectHistoryContext NotARepository { get; } = new(false, "-");
    public string BranchDisplay => string.IsNullOrWhiteSpace(Branch) ? "detached" : Branch;
}
