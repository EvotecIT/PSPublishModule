namespace PowerForge;

/// <summary>
/// Options controlling the end-to-end formatting pipeline.
/// </summary>
public sealed class FormatOptions
{
    /// <summary>When true, removes comments inside the param(...) block.</summary>
    public bool RemoveCommentsInParamBlock { get; set; }
    /// <summary>When true, removes comments appearing before the param(...) block.</summary>
    public bool RemoveCommentsBeforeParamBlock { get; set; }
    /// <summary>When true, removes all empty lines.</summary>
    public bool RemoveAllEmptyLines { get; set; }
    /// <summary>When true, collapses consecutive empty lines to a single empty line.</summary>
    public bool RemoveEmptyLines { get; set; }
    /// <summary>Optional PSScriptAnalyzer settings as JSON.</summary>
    public string? PssaSettingsJson { get; set; }
    /// <summary>Timeout for the whole run in seconds (default 120).</summary>
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Target line ending after formatting (default CRLF).</summary>
    public LineEnding LineEnding { get; set; } = LineEnding.CRLF;
    /// <summary>Whether to save UTF-8 with BOM after formatting (recommended true for PS 5.1 compatibility).</summary>
    public bool Utf8Bom { get; set; } = true;
}
