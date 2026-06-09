using OfficeIMO.Markdown;

namespace PowerForge.Blazor;

/// <summary>
/// Renders markdown content to HTML using OfficeIMO.Markdown.
/// </summary>
public class MarkdownRenderer : IDocumentationRenderer
{
    private static readonly MarkdownReaderOptions GitHubLikeReaderOptions = new()
    {
        // GitHub-flavored markdown does not support definition lists by default.
        DefinitionLists = false
    };

    private readonly MarkdownRendererOptions _options;

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "markdown", "md" };

    public MarkdownRenderer(MarkdownRendererOptions? options = null)
    {
        _options = options ?? new MarkdownRendererOptions();
    }

    public string RenderToHtml(string content, string contentType, RenderOptions? options = null)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        try
        {
            var doc = MarkdownReader.Parse(content, GitHubLikeReaderOptions);
            var htmlOptions = BuildHtmlOptions(options);
            var html = doc.ToHtmlFragment(htmlOptions);

            // Apply wrapper class if specified
            if (!string.IsNullOrEmpty(options?.WrapperClass))
            {
                html = $"<div class=\"{options.WrapperClass}\">{html}</div>";
            }

            return html;
        }
        catch (Exception ex)
        {
            // Fallback to escaped content on parse error
            return $"<pre class=\"markdown-error\">Error rendering markdown: {System.Web.HttpUtility.HtmlEncode(ex.Message)}</pre><pre>{System.Web.HttpUtility.HtmlEncode(content)}</pre>";
        }
    }

    public Task<string> RenderToHtmlAsync(string content, string contentType, RenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RenderToHtml(content, contentType, options));
    }

    private HtmlOptions BuildHtmlOptions(RenderOptions? options)
    {
        var htmlOptions = new HtmlOptions
        {
            Kind = HtmlKind.Fragment
        };

        // Configure PrismJS for syntax highlighting
        if (options?.EnableSyntaxHighlighting ?? _options.EnableSyntaxHighlighting)
        {
            htmlOptions.Prism = new PrismOptions
            {
                Enabled = true,
                Theme = _options.PrismTheme
            };
        }

        return htmlOptions;
    }
}

/// <summary>
/// Options for MarkdownRenderer.
/// </summary>
public class MarkdownRendererOptions
{
    /// <summary>
    /// Whether to enable syntax highlighting for code blocks. Defaults to true.
    /// </summary>
    public bool EnableSyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// Prism theme to use. Defaults to Tomorrow.
    /// </summary>
    public PrismTheme PrismTheme { get; set; } = PrismTheme.GithubAuto;

    /// <summary>
    /// Default CSS class for rendered content.
    /// </summary>
    public string? DefaultWrapperClass { get; set; } = "markdown-body";
}
