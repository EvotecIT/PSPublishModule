namespace PowerForgeStudio.Domain.Hub;

public sealed record GitWorktreeEntry(
    string Path,
    string? Branch,
    bool IsLocked,
    bool IsBare)
{
    public string BranchDisplay => Branch ?? "(detached)";
}
