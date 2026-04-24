namespace PowerForge.Web;

/// <summary>Social metadata used for OpenGraph and Twitter cards.</summary>
public sealed class SocialSpec
{
    /// <summary>When true, emit social metadata.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Optional site name override.</summary>
    public string? SiteName { get; set; }
    /// <summary>Default preview image URL.</summary>
    public string? Image { get; set; }
    /// <summary>Optional default preview image width in pixels.</summary>
    public int? ImageWidth { get; set; }
    /// <summary>Optional default preview image height in pixels.</summary>
    public int? ImageHeight { get; set; }
    /// <summary>Twitter card type (summary, summary_large_image, etc.).</summary>
    public string? TwitterCard { get; set; } = "summary";
    /// <summary>Optional Twitter site handle (for example: @myproject).</summary>
    public string? TwitterSite { get; set; }
    /// <summary>Optional Twitter creator handle (for example: @author).</summary>
    public string? TwitterCreator { get; set; }
    /// <summary>When true, generate per-page social card PNG files from page content.</summary>
    public bool AutoGenerateCards { get; set; }
    /// <summary>Output URL prefix for generated social card files.</summary>
    public string? GeneratedCardsPath { get; set; } = "/assets/social/generated";
    /// <summary>Width of generated social card PNG files.</summary>
    public int GeneratedCardWidth { get; set; } = 1200;
    /// <summary>Height of generated social card PNG files.</summary>
    public int GeneratedCardHeight { get; set; } = 630;
    /// <summary>Optional default style key for generated social cards (home/default/docs/api/blog/contact).</summary>
    public string? GeneratedCardStyle { get; set; }
    /// <summary>Optional default layout variant for generated social cards (product/spotlight/shelf/reference/editorial/inline-image/connect).</summary>
    public string? GeneratedCardVariant { get; set; }
    /// <summary>Optional named theme applied to generated social cards.</summary>
    public string? GeneratedCardTheme { get; set; }
    /// <summary>Optional default logo URL/path for generated social cards.</summary>
    public string? GeneratedCardLogo { get; set; }
    /// <summary>Reusable named themes for generated social cards.</summary>
    public Dictionary<string, SocialCardThemeSpec>? GeneratedCardThemes { get; set; }
    /// <summary>Optional per-collection named theme overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardThemesByCollection { get; set; }
    /// <summary>Optional per-collection style overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardStylesByCollection { get; set; }
    /// <summary>Optional per-collection variant overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardVariantsByCollection { get; set; }
    /// <summary>Optional default color scheme for generated social cards (dark/light).</summary>
    public string? GeneratedCardColorScheme { get; set; }
    /// <summary>Optional per-collection color scheme overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardColorSchemesByCollection { get; set; }
    /// <summary>When true, generated social card PNGs may fetch remote HTTP(S) logo/media assets.</summary>
    public bool GeneratedCardAllowRemoteMediaFetch { get; set; }
    /// <summary>Optional default metric chips rendered on generated social cards.</summary>
    public List<SocialCardMetricSpec>? GeneratedCardMetrics { get; set; }
}

/// <summary>Reusable generated social card theme defaults.</summary>
public sealed class SocialCardThemeSpec
{
    /// <summary>Optional style key for this theme (home/default/docs/api/blog/contact).</summary>
    public string? Style { get; set; }
    /// <summary>Optional layout variant for this theme (product/spotlight/shelf/reference/editorial/inline-image/connect).</summary>
    public string? Variant { get; set; }
    /// <summary>Optional color scheme for this theme (dark/light).</summary>
    public string? ColorScheme { get; set; }
    /// <summary>Optional logo URL/path for this theme.</summary>
    public string? Logo { get; set; }
    /// <summary>When set, overrides the site default for fetching remote HTTP(S) logo/media assets.</summary>
    public bool? AllowRemoteMediaFetch { get; set; }
    /// <summary>Theme token overrides merged over the active theme manifest tokens for generated social cards.</summary>
    public Dictionary<string, object?>? Tokens { get; set; }
    /// <summary>Optional metric chips rendered when this named theme is selected.</summary>
    public List<SocialCardMetricSpec>? Metrics { get; set; }
}

/// <summary>Small data point rendered on generated social cards (for example stars, issues, downloads).</summary>
public sealed class SocialCardMetricSpec
{
    /// <summary>Optional short icon or glyph label. Keep to one or two characters for portability.</summary>
    public string? Icon { get; set; }
    /// <summary>Primary metric value (for example 8k, 55, 1.2M).</summary>
    public string? Value { get; set; }
    /// <summary>Secondary metric label (for example Stars, Issues, Downloads).</summary>
    public string? Label { get; set; }
    /// <summary>Optional per-metric color override.</summary>
    public string? Color { get; set; }
}
