using AngleSharp.Dom;

namespace PowerForge.Web;

/// <summary>Runs SEO-focused checks on generated HTML output.</summary>
public static partial class WebSeoDoctor
{
    private static bool IsGeneratedApiReferencePage(string relativePath, IDocument doc)
    {
        if (doc.Body is null)
            return false;

        return ContainsClassToken(doc.Body.GetAttribute("class"), "pf-api-docs") ||
               HasPowerForgeApiDocsMarker(doc);
    }

    private static bool HasPowerForgeApiDocsMarker(IDocument doc)
    {
        return doc.Head?.QuerySelector("meta[name='generator'][data-pf='api-docs']") is not null;
    }

    private static bool ContainsClassToken(string? classValue, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(classValue) || string.IsNullOrWhiteSpace(expectedToken))
            return false;

        return classValue
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => token.Equals(expectedToken, StringComparison.OrdinalIgnoreCase));
    }
}
