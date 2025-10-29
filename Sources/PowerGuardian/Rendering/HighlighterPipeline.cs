using System.Collections.Generic;
using System.Linq;
using PowerGuardian.Rendering.Highlighters;

namespace PowerGuardian.Rendering;

internal sealed class HighlighterPipeline
{
    private readonly List<IHighlighter> _highlighters;

    public HighlighterPipeline()
    {
        _highlighters = new List<IHighlighter>
        {
            // Order matters: specific tokenizers first, then generic fallback
            new PowerShellHighlighter(),
            new Highlighters.JsonHighlighter(),
            new GenericHighlighter()
        };
    }

    public string Highlight(string code, string language)
    {
        var lang = (language ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var h in _highlighters)
        {
            if (h.CanHandle(lang))
            {
                var s = h.Highlight(code, lang);
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        // Fallback (should not reach due to GenericHighlighter)
        return code ?? string.Empty;
    }
}
