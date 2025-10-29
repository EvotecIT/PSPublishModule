namespace PowerGuardian.Rendering;

internal interface IHighlighter
{
    bool CanHandle(string language);
    // Returns Spectre markup; when unable to highlight, return null to allow fallback
    string Highlight(string code, string language);
}

