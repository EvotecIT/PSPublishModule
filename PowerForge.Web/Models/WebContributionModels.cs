using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for validating or importing website contribution bundles.</summary>
public sealed class WebContributionOptions
{
    /// <summary>Root of the contribution repository.</summary>
    public string SourceRoot { get; set; } = string.Empty;
    /// <summary>Destination website root for import operations.</summary>
    public string? SiteRoot { get; set; }
    /// <summary>When true, import accepted bundles into the destination website.</summary>
    public bool Import { get; set; }
    /// <summary>Overwrite existing destination files during import.</summary>
    public bool Force { get; set; }
    /// <summary>Remove draft markers during import.</summary>
    public bool Publish { get; set; }
    /// <summary>Relative posts root inside the contribution repository.</summary>
    public string PostsPath { get; set; } = "posts";
    /// <summary>Relative authors root inside the contribution repository.</summary>
    public string AuthorsPath { get; set; } = "authors";
    /// <summary>Relative destination blog content root inside the website repository.</summary>
    public string ContentBlogPath { get; set; } = "content/blog";
    /// <summary>Relative destination asset root inside the website repository.</summary>
    public string StaticBlogAssetsPath { get; set; } = "static/assets/blog";
    /// <summary>Relative destination authors root inside the website repository.</summary>
    public string TargetAuthorsPath { get; set; } = "data/authors";
    /// <summary>Maximum allowed size for a single contributed asset.</summary>
    public long MaxAssetBytes { get; set; } = 5 * 1024 * 1024;
    /// <summary>Maximum total size for all assets in one post bundle.</summary>
    public long MaxPostAssetBytes { get; set; } = 30 * 1024 * 1024;
}

/// <summary>Author profile accepted by website contribution import.</summary>
public sealed class WebContributionAuthorProfile
{
    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    /// <summary>Stable author slug used by post front matter.</summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;
    /// <summary>Short title, role, or affiliation.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    /// <summary>Short biography for author surfaces.</summary>
    [JsonPropertyName("bio")]
    public string? Bio { get; set; }
    /// <summary>Avatar URL or site-rooted avatar path.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
    /// <summary>X/Twitter profile URL or handle.</summary>
    [JsonPropertyName("x")]
    public string? X { get; set; }
    /// <summary>LinkedIn profile URL.</summary>
    [JsonPropertyName("linkedin")]
    public string? LinkedIn { get; set; }
    /// <summary>GitHub profile URL or username.</summary>
    [JsonPropertyName("github")]
    public string? GitHub { get; set; }
    /// <summary>Personal or company website URL.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }
}

/// <summary>Author catalog imported into website data.</summary>
public sealed class WebContributionAuthorCatalog
{
    /// <summary>Known website author profiles.</summary>
    [JsonPropertyName("authors")]
    public WebContributionAuthorProfile[] Authors { get; set; } = Array.Empty<WebContributionAuthorProfile>();
}

/// <summary>Result of validating or importing website contribution bundles.</summary>
public sealed class WebContributionResult
{
    /// <summary>Whether validation/import succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Source repository root.</summary>
    public string SourceRoot { get; set; } = string.Empty;
    /// <summary>Destination website root, when importing.</summary>
    public string? SiteRoot { get; set; }
    /// <summary>Discovered author count.</summary>
    public int AuthorCount { get; set; }
    /// <summary>Discovered post count.</summary>
    public int PostCount { get; set; }
    /// <summary>Imported post count.</summary>
    public int ImportedPostCount { get; set; }
    /// <summary>Copied asset count.</summary>
    public int CopiedAssetCount { get; set; }
    /// <summary>Copied author profile count.</summary>
    public int CopiedAuthorCount { get; set; }
    /// <summary>Per-post validation/import results.</summary>
    public WebContributionPostResult[] Posts { get; set; } = Array.Empty<WebContributionPostResult>();
    /// <summary>Validation or import errors.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Non-fatal warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Per-post contribution bundle result.</summary>
public sealed class WebContributionPostResult
{
    /// <summary>Source index.md path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Post bundle root.</summary>
    public string BundlePath { get; set; } = string.Empty;
    /// <summary>Language code.</summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>URL/file slug.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Post title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Resolved publish year used for assets.</summary>
    public int Year { get; set; }
    /// <summary>Referenced authors.</summary>
    public string[] Authors { get; set; } = Array.Empty<string>();
    /// <summary>Resolved author display names.</summary>
    public string[] AuthorNames { get; set; } = Array.Empty<string>();
    /// <summary>Local asset files in the bundle.</summary>
    public string[] Assets { get; set; } = Array.Empty<string>();
    /// <summary>Destination markdown path, when importing.</summary>
    public string? TargetContentPath { get; set; }
    /// <summary>Destination asset root, when importing.</summary>
    public string? TargetAssetPath { get; set; }
    /// <summary>Whether this post was imported.</summary>
    public bool Imported { get; set; }
}
