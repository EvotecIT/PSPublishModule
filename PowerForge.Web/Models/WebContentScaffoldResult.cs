namespace PowerForge.Web;

/// <summary>Result payload for content scaffolding.</summary>
public sealed class WebContentScaffoldResult
{
    /// <summary>Output file path.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Collection name.</summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Slug used for the file.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>When true, the file was created.</summary>
    public bool Created { get; set; }
}
