using System;

namespace PowerGuardian;

internal enum RepoHost
{
    Unknown,
    GitHub,
    AzureDevOps
}

internal sealed class RepoInfo
{
    public RepoHost Host { get; set; } = RepoHost.Unknown;
    public Uri ProjectUri { get; set; }
    // GitHub
    public string Owner { get; set; }
    public string Repo { get; set; }
    // Azure DevOps
    public string Organization { get; set; }
    public string Project { get; set; }
    public string Repository { get; set; }
    public string Branch { get; set; }
}

