namespace PowerForge.Web;

public sealed class ShortcodeRenderContext
{
    public SiteSpec Site { get; init; } = new();
    public FrontMatter? FrontMatter { get; init; }
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public ThemeManifest? ThemeManifest { get; init; }
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
