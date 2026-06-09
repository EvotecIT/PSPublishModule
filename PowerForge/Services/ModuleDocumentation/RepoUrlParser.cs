using System;
using System.Text.RegularExpressions;

namespace PowerForge;

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
                    Owner = DecodeSegment(parts[0]),
                    Repo = TrimGitSuffix(DecodeSegment(parts[1]))
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
                    Organization = DecodeSegment(parts[0]),
                    Project = DecodeSegment(parts[1]),
                    Repository = TrimGitSuffix(DecodeSegment(parts[3]))
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
                    Project = DecodeSegment(parts[0]),
                    Repository = TrimGitSuffix(DecodeSegment(parts[2]))
                };
            }
        }

        return new RepoInfo { Host = RepoHost.Unknown, ProjectUri = uri };
    }

    private static string TrimGitSuffix(string value)
        => Regex.Replace(value ?? string.Empty, "\\.git$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string DecodeSegment(string value)
        => Uri.UnescapeDataString(value ?? string.Empty);
}
