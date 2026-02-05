namespace PowerForge.Web;

/// <summary>Defines output formats and routing rules.</summary>
public sealed class OutputsSpec
{
    /// <summary>Available output formats.</summary>
    public OutputFormatSpec[] Formats { get; set; } = Array.Empty<OutputFormatSpec>();

    /// <summary>Per-kind output rules.</summary>
    public OutputRuleSpec[] Rules { get; set; } = Array.Empty<OutputRuleSpec>();
}

/// <summary>Defines a named output format (html/rss/json).</summary>
public sealed class OutputFormatSpec
{
    /// <summary>Format name (html, rss, json).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>MIME type for the format.</summary>
    public string MediaType { get; set; } = "text/html";

    /// <summary>File suffix (html, xml, json).</summary>
    public string Suffix { get; set; } = "html";

    /// <summary>Optional rel attribute for link tags.</summary>
    public string? Rel { get; set; }

    /// <summary>When true, render as plain text.</summary>
    public bool IsPlainText { get; set; }
}

/// <summary>Defines output formats for a page kind.</summary>
public sealed class OutputRuleSpec
{
    /// <summary>Page kind (page, section, taxonomy, term, home).</summary>
    public string Kind { get; set; } = "page";

    /// <summary>Enabled formats for the kind.</summary>
    public string[] Formats { get; set; } = Array.Empty<string>();
}
