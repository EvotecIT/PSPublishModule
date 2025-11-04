using System;
using System.Text.RegularExpressions;

namespace PSMaintenance;

/// <summary>
/// Parses repository URLs (GitHub/Azure DevOps) into <see cref="RepoInfo"/>.
/// </summary>
internal static class RepoUrlParser
{
    public static RepoInfo Parse(string projectUri)
    {
        if (string.IsNullOrWhiteSpace(projectUri)) return new RepoInfo { Host = RepoHost.Unknown };
        if (!Uri.TryCreate(projectUri, UriKind.Absolute, out var uri)) return new RepoInfo { Host = RepoHost.Unknown };

        // GitHub patterns: https://github.com/{owner}/{repo}
        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 2)
            {
                return new RepoInfo
                {
                    Host = RepoHost.GitHub,
                    ProjectUri = uri,
                    Owner = parts[0],
                    Repo = parts[1]
                };
            }
        }

        // Azure DevOps (new): https://dev.azure.com/{org}/{project}/_git/{repo}
        if (string.Equals(uri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            // org/project/_git/repo
            if (parts.Length >= 4 && string.Equals(parts[2], "_git", StringComparison.OrdinalIgnoreCase))
            {
                return new RepoInfo
                {
                    Host = RepoHost.AzureDevOps,
                    ProjectUri = uri,
                    Organization = parts[0],
                    Project = parts[1],
                    Repository = parts[3]
                };
            }
        }

        // Azure DevOps (legacy): https://{org}.visualstudio.com/{project}/_git/{repo}
        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var org = uri.Host.Substring(0, uri.Host.IndexOf('.'));
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 3 && string.Equals(parts[1], "_git", StringComparison.OrdinalIgnoreCase))
            {
                return new RepoInfo
                {
                    Host = RepoHost.AzureDevOps,
                    ProjectUri = uri,
                    Organization = org,
                    Project = parts[0],
                    Repository = parts[2]
                };
            }
        }

        return new RepoInfo { Host = RepoHost.Unknown, ProjectUri = uri };
    }
}
