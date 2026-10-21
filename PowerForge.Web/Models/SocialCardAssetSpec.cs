namespace PowerForge.Web;

/// <summary>A self-hosted font used by the social card renderer without modifying machine font configuration.</summary>
public sealed class SocialCardFontSpec
{
    /// <summary>Family name referenced by socialCard.fontDisplay, fontBody, or fontMono tokens.</summary>
    public string Family { get; set; } = string.Empty;
    /// <summary>CSS weight (for example 700) or a variable font range (for example "100 800").</summary>
    public object? Weight { get; set; } = 400;
    /// <summary>TTF/OTF path relative to the site root.</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Resolves page-specific artwork from existing front matter or a route slug.</summary>
public sealed class SocialCardImageSpec
{
    /// <summary>Image path containing {meta.key}, {slug}, or {title} placeholders.</summary>
    public string? Pattern { get; set; }
    /// <summary>Optional image path used when a placeholder or file is missing.</summary>
    public string? Fallback { get; set; }
}
