namespace PowerForge.Web;

/// <summary>Represents an opt-in rendered page assertion for SEO doctor checks.</summary>
public sealed class WebSeoDoctorPageAssertion
{
    /// <summary>Relative output path or route-like page path to validate.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Optional friendly label used in diagnostics.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>When true, the asserted page must exist.</summary>
    public bool MustExist { get; set; } = true;
    /// <summary>Text snippets that must appear in the selected page scope.</summary>
    public string[] Contains { get; set; } = Array.Empty<string>();
    /// <summary>Text snippets that must not appear in the selected page scope.</summary>
    public string[] NotContains { get; set; } = Array.Empty<string>();
    /// <summary>Content scope to inspect. Supported values are <c>body</c>, <c>rendered</c>, and <c>html</c>.</summary>
    public string Scope { get; set; } = "body";
}
