using System;

namespace PSMaintenance;

/// <summary>
/// Identifies a supported repository host and parsed coordinates for fetching files.
/// </summary>
internal enum RepoHost
{
    /// <summary>Unknown or unsupported host.</summary>
    Unknown,
    /// <summary>GitHub (github.com) repositories.</summary>
    GitHub,
    /// <summary>Azure DevOps repositories (dev.azure.com or *.visualstudio.com).</summary>
    AzureDevOps
}

/// <summary>
/// Parsed repository information derived from PrivateData.PSData.ProjectUri.
/// </summary>
internal sealed class RepoInfo
{
    /// <summary>Repository host type.</summary>
    public RepoHost Host { get; set; } = RepoHost.Unknown;
    /// <summary>Original project URI.</summary>
    public Uri? ProjectUri { get; set; }
    // GitHub
    /// <summary>GitHub owner (account or organization).</summary>
    public string? Owner { get; set; }
    /// <summary>GitHub repository name.</summary>
    public string? Repo { get; set; }
    // Azure DevOps
    /// <summary>Azure DevOps organization.</summary>
    public string? Organization { get; set; }
    /// <summary>Azure DevOps project.</summary>
    public string? Project { get; set; }
    /// <summary>Azure DevOps repository.</summary>
    public string? Repository { get; set; }
    /// <summary>Branch to use when fetching files.</summary>
    public string? Branch { get; set; }
}
