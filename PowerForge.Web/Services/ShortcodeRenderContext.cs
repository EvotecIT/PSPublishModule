namespace PowerForge.Web;

/// <summary>Runtime context passed into shortcode rendering.</summary>
public sealed class ShortcodeRenderContext
{
    /// <summary>Resolved site configuration.</summary>
    public SiteSpec Site { get; init; } = new();
    /// <summary>Front matter for the current page, if any.</summary>
    public FrontMatter? FrontMatter { get; init; }
    /// <summary>Data dictionary available to shortcodes.</summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    /// <summary>Resolved theme manifest.</summary>
    public ThemeManifest? ThemeManifest { get; init; }
    /// <summary>Resolved theme root path.</summary>
    public string? ThemeRoot { get; init; }
    internal ITemplateEngine? Engine { get; init; }
    internal Func<string, string?>? PartialResolver { get; init; }

    internal static ShortcodeRenderContext FromDataOnly(IReadOnlyDictionary<string, object?> data)
    {
        return new ShortcodeRenderContext
        {
            Data = data ?? new Dictionary<string, object?>()
        };
    }

    internal string? TryRenderThemeShortcode(string name, Dictionary<string, string> attrs)
    {
        if (Engine is null || PartialResolver is null)
            return null;

        var template = PartialResolver($"shortcodes/{name}") ?? PartialResolver(name);
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var shortcodeContext = new ShortcodeContext
        {
            Name = name,
            Attrs = attrs,
            Data = ShortcodeProcessor.ResolveList(Data, attrs)
        };

        var page = new ContentItem
        {
            Title = FrontMatter?.Title ?? string.Empty,
            Description = FrontMatter?.Description ?? string.Empty,
            Meta = FrontMatter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };

        var renderContext = new ThemeRenderContext
        {
            Site = Site,
            Page = page,
            Data = Data,
            Shortcode = shortcodeContext
        };

        return Engine.Render(template, renderContext, PartialResolver);
    }
}
