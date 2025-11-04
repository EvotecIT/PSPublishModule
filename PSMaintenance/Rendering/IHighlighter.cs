namespace PSMaintenance.Rendering;

/// <summary>
/// Provides syntax highlighting for fenced code blocks.
/// </summary>
internal interface IHighlighter
{
    /// <summary>Returns true if the highlighter can handle the given language id.</summary>
    bool CanHandle(string language);
    /// <summary>
    /// Produces Spectre markup for the given code. When unable to highlight, return null to allow pipeline fallback.
    /// </summary>
    string? Highlight(string code, string language);
}
