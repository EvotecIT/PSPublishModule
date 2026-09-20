namespace PowerForgeStudio.Domain.Hub;

/// <summary>A remote changed-file record. Paths are display data, never local file-operation targets.</summary>
public sealed record GitHubPullRequestFile(string Path, string? PreviousPath, string Status, int Additions, int Deletions,
    string? Patch, bool PatchTruncated)
{
    public string Summary => $"{Status} · +{Additions} / -{Deletions}";
    public string PatchNotice => Patch is null ? "GitHub did not provide a text patch. The file may be binary or too large. Open the PR on GitHub to inspect it."
        : PatchTruncated ? "Preview limited to 262,144 characters. Open the PR on GitHub for the remaining changes."
        : "GitHub patch excerpt; unchanged context and some large-file changes may be omitted.";
}

/// <summary>File listing whose head and base commits were checked before and after pagination.</summary>
public sealed record GitHubPullRequestFiles(string HeadSha, string BaseSha, GitHubPage<GitHubPullRequestFile> Files);
