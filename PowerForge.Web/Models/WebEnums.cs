namespace PowerForge.Web;

/// <summary>Controls trailing slash handling for generated URLs.</summary>
public enum TrailingSlashMode
{
    /// <summary>Always append a trailing slash.</summary>
    Always,
    /// <summary>Never append a trailing slash.</summary>
    Never,
    /// <summary>Leave existing slashes unchanged.</summary>
    Ignore
}

/// <summary>Controls sorting direction.</summary>
public enum SortOrder
{
    /// <summary>Ascending order.</summary>
    Asc,
    /// <summary>Descending order.</summary>
    Desc
}

/// <summary>Redirect match strategy.</summary>
public enum RedirectMatchType
{
    /// <summary>Exact path match.</summary>
    Exact,
    /// <summary>Prefix match.</summary>
    Prefix,
    /// <summary>Wildcard match.</summary>
    Wildcard,
    /// <summary>Regular expression match.</summary>
    Regex
}

/// <summary>Analytics provider selection.</summary>
public enum AnalyticsProvider
{
    /// <summary>No analytics.</summary>
    None,
    /// <summary>First-party analytics collector.</summary>
    FirstParty
}

/// <summary>API documentation source type.</summary>
public enum ApiDocsType
{
    /// <summary>C# XML documentation.</summary>
    CSharp,
    /// <summary>PowerShell help output.</summary>
    PowerShell
}

/// <summary>Changelog source selection.</summary>
public enum WebChangelogSource
{
    /// <summary>Use local changelog when available, otherwise fall back to repository releases.</summary>
    Auto,
    /// <summary>Parse a local changelog file.</summary>
    File,
    /// <summary>Pull releases from a repository API.</summary>
    GitHub
}

/// <summary>Repository provider selection.</summary>
public enum RepositoryProvider
{
    /// <summary>GitHub.</summary>
    GitHub,
    /// <summary>Azure DevOps.</summary>
    AzureDevOps,
    /// <summary>GitLab.</summary>
    GitLab,
    /// <summary>Other provider.</summary>
    Other
}

/// <summary>Package registry selection.</summary>
public enum PackageType
{
    /// <summary>NuGet package.</summary>
    NuGet,
    /// <summary>PowerShell Gallery module.</summary>
    PowerShellGallery,
    /// <summary>Other registry.</summary>
    Other
}

/// <summary>Page kind used for templating and outputs.</summary>
public enum PageKind
{
    /// <summary>Regular content page.</summary>
    Page,
    /// <summary>Section/list page.</summary>
    Section,
    /// <summary>Taxonomy list page.</summary>
    Taxonomy,
    /// <summary>Taxonomy term page.</summary>
    Term,
    /// <summary>Home page.</summary>
    Home
}
