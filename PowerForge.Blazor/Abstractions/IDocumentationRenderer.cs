namespace PowerForge.Blazor;

/// <summary>
/// Renders documentation content to HTML. Different renderers can handle
/// different content types (Markdown, XML, etc.).
/// </summary>
public interface IDocumentationRenderer
{
    /// <summary>
    /// Content types this renderer supports (e.g., "markdown", "xml", "html").
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Renders content to HTML.
    /// </summary>
    /// <param name="content">The raw content to render.</param>
    /// <param name="contentType">The content type (e.g., "markdown").</param>
    /// <param name="options">Optional rendering options.</param>
    /// <returns>Rendered HTML string.</returns>
    string RenderToHtml(string content, string contentType, RenderOptions? options = null);

    /// <summary>
    /// Asynchronously renders content to HTML.
    /// </summary>
    Task<string> RenderToHtmlAsync(string content, string contentType, RenderOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for rendering documentation content.
/// </summary>
public class RenderOptions
{
    /// <summary>
    /// Base path for resolving relative links and images.
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Whether to include syntax highlighting for code blocks.
    /// </summary>
    public bool EnableSyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// Whether to generate a table of contents from headings.
    /// </summary>
    public bool GenerateToc { get; set; } = false;

    /// <summary>
    /// CSS class to wrap rendered content.
    /// </summary>
    public string? WrapperClass { get; set; }

    /// <summary>
    /// Custom CSS variables or theme overrides.
    /// </summary>
    public Dictionary<string, string>? ThemeOverrides { get; set; }
}
