using Spectre.Console;

namespace PSMaintenance.Rendering.Highlighters;

/// <summary>
/// Generic fallback highlighter that escapes markup without styling.
/// </summary>
internal sealed class GenericHighlighter : IHighlighter
{
    public bool CanHandle(string language) => true; // always can fall back
    public string? Highlight(string code, string language)
    {
        return Markup.Escape(code ?? string.Empty);
    }
}
