using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Minimal abstraction for repository content operations used by the planner.
/// </summary>
internal interface IRepoClient
{
    string GetDefaultBranch();
    string? GetFileContent(string path, string branch);
    List<(string Name, string Path)> ListFiles(string path, string branch);
    /// <summary>Lists releases (tag, name, body, published, assets) when supported; returns empty when not implemented.</summary>
    List<RepoRelease> ListReleases();
}

internal static class RepoClientFactory
{
    public static IRepoClient? Create(RepoInfo info, string? token)
    {
        switch (info.Host)
        {
            case RepoHost.GitHub:
                if (string.IsNullOrEmpty(info.Owner) || string.IsNullOrEmpty(info.Repo)) return null;
                return new GitHubRepository(info.Owner!, info.Repo!, token);
            case RepoHost.AzureDevOps:
                if (string.IsNullOrEmpty(info.Organization) || string.IsNullOrEmpty(info.Project) || string.IsNullOrEmpty(info.Repository)) return null;
                return new AzureDevOpsRepository(info.Organization!, info.Project!, info.Repository!, token);
            default:
                return null;
        }
    }
}
