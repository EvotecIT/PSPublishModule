namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryWorkspaceFamilySnapshot(
    string FamilyKey,
    string DisplayName,
    string? PrimaryRootPath,
    int TotalMembers,
    int WorktreeMembers,
    int AttentionMembers,
    int ReadyMembers,
    int QueueActiveMembers,
    string MemberSummary)
{
    public string CountDisplay => TotalMembers.ToString();

    public string StatusDisplay => $"{WorktreeMembers} worktree(s) | {AttentionMembers} attention | {QueueActiveMembers} active";
}
