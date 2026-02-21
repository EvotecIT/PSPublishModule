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
    /// <summary>Optional default style key for generated social cards (default/docs/api/editorial).</summary>
    public string? GeneratedCardStyle { get; set; }
    /// <summary>Optional default layout variant for generated social cards (standard/hero/compact).</summary>
    public string? GeneratedCardVariant { get; set; }
    /// <summary>Optional per-collection style overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardStylesByCollection { get; set; }
    /// <summary>Optional per-collection variant overrides for generated social cards.</summary>
    public Dictionary<string, string>? GeneratedCardVariantsByCollection { get; set; }
}
