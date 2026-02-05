namespace PowerForge.Web;

/// <summary>Project-specific metadata for multi‑project sites.</summary>
public sealed class ProjectSpec
{
    /// <summary>Schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Project display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Project slug.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Optional theme override.</summary>
    public string? Theme { get; set; }

    /// <summary>Repository metadata.</summary>
    public RepositorySpec? Repository { get; set; }
    /// <summary>Associated packages.</summary>
    public PackageSpec[] Packages { get; set; } = Array.Empty<PackageSpec>();
    /// <summary>External or internal links.</summary>
    public LinkSpec[] Links { get; set; } = Array.Empty<LinkSpec>();
    /// <summary>API docs configuration.</summary>
    public ApiDocsSpec? ApiDocs { get; set; }
    /// <summary>Project‑level redirects.</summary>
    public RedirectSpec[] Redirects { get; set; } = Array.Empty<RedirectSpec>();
    /// <summary>Project edit link configuration.</summary>
    public EditLinksSpec? EditLinks { get; set; }
    /// <summary>Project content filters.</summary>
    public ProjectContentSpec? Content { get; set; }
}

/// <summary>Repository location details.</summary>
public sealed class RepositorySpec
{
    /// <summary>Repository provider.</summary>
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.Other;
    /// <summary>Repository owner/org.</summary>
    public string? Owner { get; set; }
    /// <summary>Repository name.</summary>
    public string? Name { get; set; }
    /// <summary>Default branch.</summary>
    public string? Branch { get; set; }
    /// <summary>Optional path base within the repo.</summary>
    public string? PathBase { get; set; }
}

/// <summary>Package metadata.</summary>
public sealed class PackageSpec
{
    /// <summary>Package type.</summary>
    public PackageType Type { get; set; } = PackageType.Other;
    /// <summary>Package identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Optional package version.</summary>
    public string? Version { get; set; }
}

/// <summary>Generic link entry.</summary>
public sealed class LinkSpec
{
    /// <summary>Link title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Link URL.</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>API documentation configuration for a project.</summary>
public sealed class ApiDocsSpec
{
    /// <summary>Docs format/type.</summary>
    public ApiDocsType Type { get; set; } = ApiDocsType.CSharp;
    /// <summary>Assembly path for reflection.</summary>
    public string? AssemblyPath { get; set; }
    /// <summary>XML doc file path.</summary>
    public string? XmlDocPath { get; set; }
    /// <summary>PowerShell help path.</summary>
    public string? HelpPath { get; set; }
    /// <summary>Docs output path.</summary>
    public string OutputPath { get; set; } = string.Empty;
}

/// <summary>Include/exclude patterns for project content.</summary>
public sealed class ProjectContentSpec
{
    /// <summary>Include glob patterns.</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Exclude glob patterns.</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
}
