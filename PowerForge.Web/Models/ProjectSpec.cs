namespace PowerForge.Web;

public sealed class ProjectSpec
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Theme { get; set; }

    public RepositorySpec? Repository { get; set; }
    public PackageSpec[] Packages { get; set; } = Array.Empty<PackageSpec>();
    public LinkSpec[] Links { get; set; } = Array.Empty<LinkSpec>();
    public ApiDocsSpec? ApiDocs { get; set; }
    public RedirectSpec[] Redirects { get; set; } = Array.Empty<RedirectSpec>();
    public EditLinksSpec? EditLinks { get; set; }
    public ProjectContentSpec? Content { get; set; }
}

public sealed class RepositorySpec
{
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.Other;
    public string? Owner { get; set; }
    public string? Name { get; set; }
    public string? Branch { get; set; }
    public string? PathBase { get; set; }
}

public sealed class PackageSpec
{
    public PackageType Type { get; set; } = PackageType.Other;
    public string Id { get; set; } = string.Empty;
    public string? Version { get; set; }
}

public sealed class LinkSpec
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class ApiDocsSpec
{
    public ApiDocsType Type { get; set; } = ApiDocsType.CSharp;
    public string? AssemblyPath { get; set; }
    public string? XmlDocPath { get; set; }
    public string? HelpPath { get; set; }
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class ProjectContentSpec
{
    public string[] Include { get; set; } = Array.Empty<string>();
    public string[] Exclude { get; set; } = Array.Empty<string>();
}
