using AngleSharp.Dom;

namespace PowerForge.Web;

/// <summary>Runs SEO-focused checks on generated HTML output.</summary>
public static partial class WebSeoDoctor
{
    private static bool IsGeneratedApiReferencePage(string relativePath, IDocument doc)
    {
        if (doc.Body is null)
            return false;

        if (ContainsClassToken(doc.Body.GetAttribute("class"), "pf-api-docs"))
            return true;

        return HasPowerForgeApiDocsMarker(doc) && IsApiReferenceRoute(relativePath);
    }

    private static bool HasPowerForgeApiDocsMarker(IDocument doc)
    {
        return doc.Head?.QuerySelector("meta[name='generator'][data-pf='api-docs']") is not null;
    }

    private static bool IsApiReferenceRoute(string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("api/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("docs/api/", StringComparison.OrdinalIgnoreCase);
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
