namespace PowerForge.Web;

public enum TrailingSlashMode
{
    Always,
    Never,
    Ignore
}

public enum SortOrder
{
    Asc,
    Desc
}

public enum RedirectMatchType
{
    Exact,
    Prefix,
    Wildcard,
    Regex
}

public enum AnalyticsProvider
{
    None,
    FirstParty
}

public enum ApiDocsType
{
    CSharp,
    PowerShell
}

public enum RepositoryProvider
{
    GitHub,
    AzureDevOps,
    GitLab,
    Other
}

public enum PackageType
{
    NuGet,
    PowerShellGallery,
    Other
}
