namespace PowerForge.Web;

/// <summary>Per-section media/embed audit profile.</summary>
public sealed class WebAuditMediaProfile
{
    /// <summary>Glob match pattern for site-relative HTML path (for example "api/**").</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>When true, media/embed checks are skipped for matching pages.</summary>
    public bool Ignore { get; set; }
    /// <summary>When true, do not require YouTube embeds to use youtube-nocookie.com.</summary>
    public bool AllowYoutubeStandardHost { get; set; }
    /// <summary>Require iframe embeds to use loading="lazy".</summary>
    public bool? RequireIframeLazy { get; set; }
    /// <summary>Require iframe embeds to provide title attributes.</summary>
    public bool? RequireIframeTitle { get; set; }
    /// <summary>Require external iframe embeds to provide referrerpolicy.</summary>
    public bool? RequireIframeReferrerPolicy { get; set; }
    /// <summary>Require images to include loading/fetchpriority hints.</summary>
    public bool? RequireImageLoadingHint { get; set; }
    /// <summary>Require images to include decoding hints.</summary>
    public bool? RequireImageDecodingHint { get; set; }
    /// <summary>Require images to include width/height or aspect-ratio hints.</summary>
    public bool? RequireImageDimensions { get; set; }
    /// <summary>Require images with srcset to include sizes.</summary>
    public bool? RequireImageSrcSetSizes { get; set; }
    /// <summary>Maximum allowed eager-loaded image count for matching pages (null uses default 1).</summary>
    public int? MaxEagerImages { get; set; }
}

