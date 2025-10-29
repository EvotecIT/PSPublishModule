using Spectre.Console;

namespace PowerGuardian.Rendering.Highlighters;

internal sealed class GenericHighlighter : IHighlighter
{
    public bool CanHandle(string language) => true; // always can fall back
    public string Highlight(string code, string language)
    {
        return Markup.Escape(code ?? string.Empty);
    }
}

