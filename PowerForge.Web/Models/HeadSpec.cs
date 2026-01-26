namespace PowerForge.Web;

/// <summary>Global document head configuration.</summary>
public sealed class HeadSpec
{
    /// <summary>Raw HTML appended into the document head.</summary>
    public string? Html { get; set; }

    /// <summary>Optional body class applied to all pages.</summary>
    public string? BodyClass { get; set; }

    /// <summary>Structured &lt;link&gt; tags rendered before <see cref="Html"/>.</summary>
    public HeadLinkSpec[] Links { get; set; } = Array.Empty<HeadLinkSpec>();

    /// <summary>Structured &lt;meta&gt; tags rendered before <see cref="Html"/>.</summary>
    public HeadMetaSpec[] Meta { get; set; } = Array.Empty<HeadMetaSpec>();
}

/// <summary>Represents a structured &lt;link&gt; element.</summary>
public sealed class HeadLinkSpec
{
    /// <summary>Link relation (rel) value.</summary>
    public string Rel { get; set; } = string.Empty;

    /// <summary>Link href value.</summary>
    public string Href { get; set; } = string.Empty;

    /// <summary>Optional MIME type.</summary>
    public string? Type { get; set; }

    /// <summary>Optional sizes attribute.</summary>
    public string? Sizes { get; set; }

    /// <summary>Optional crossorigin attribute value.</summary>
    public string? Crossorigin { get; set; }
}

/// <summary>Represents a structured &lt;meta&gt; element.</summary>
public sealed class HeadMetaSpec
{
    /// <summary>Meta name attribute.</summary>
    public string? Name { get; set; }

    /// <summary>Meta property attribute.</summary>
    public string? Property { get; set; }

    /// <summary>Meta content attribute.</summary>
    public string Content { get; set; } = string.Empty;
}
