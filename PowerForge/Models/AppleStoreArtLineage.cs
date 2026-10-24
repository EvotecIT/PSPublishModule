namespace PowerForge;

/// <summary>Exact derived PNG linked to retained raw capture bytes. Used by capture provenance v3.</summary>
public sealed class AppleStoreArtLineage
{
    /// <summary>Output path relative to the raw capture inventory root.</summary>
    public string Path { get; set; } = "";
    /// <summary>SHA-256 of the exact composed PNG.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Output pixel width.</summary>
    public int Width { get; set; }
    /// <summary>Output pixel height.</summary>
    public int Height { get; set; }
    /// <summary>Stable dHash (9 by 8 grayscale samples), prefixed dhash-v1:.</summary>
    public string PerceptualHash { get; set; } = "";
    /// <summary>Frame id.</summary>
    public string Frame { get; set; } = "";
    /// <summary>Listing locale.</summary>
    public string Locale { get; set; } = "";
    /// <summary>Output geometry key.</summary>
    public string Device { get; set; } = "";
    /// <summary>Shipped theme name.</summary>
    public string Theme { get; set; } = "";
    /// <summary>SHA-256 of the exact spec JSON bytes.</summary>
    public string SpecSha256 { get; set; } = "";
    /// <summary>SHA-256 of the selected locale captions JSON bytes.</summary>
    public string CaptionsSha256 { get; set; } = "";
    /// <summary>SHA-256 of the native template, styles, fitting script and font resources.</summary>
    public string TemplateSha256 { get; set; } = "";
    /// <summary>SHA-256 of the final HTML used to render this frame, or raw PNG for Watch.</summary>
    public string RenderSha256 { get; set; } = "";
    /// <summary>Browser engine version, or passthrough for Watch.</summary>
    public string Renderer { get; set; } = "";
    /// <summary>Exact raw inputs. Every entry must belong to the retained capture inventory.</summary>
    public AppleStoreArtInput[] Inputs { get; set; } = Array.Empty<AppleStoreArtInput>();
}

/// <summary>Raw PNG identity bound into a composed frame.</summary>
public sealed class AppleStoreArtInput
{
    /// <summary>Capture inventory-relative path.</summary>
    public string Path { get; set; } = "";
    /// <summary>SHA-256 of the unchanged PNG.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Raw pixel width.</summary>
    public int Width { get; set; }
    /// <summary>Raw pixel height.</summary>
    public int Height { get; set; }
    /// <summary>Scenario id when referenced through the spec capture map.</summary>
    public string? Scenario { get; set; }
}
