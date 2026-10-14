namespace PowerForge.Web;

/// <summary>Distinguishes links outside the generated site from filesystem targets.</summary>
internal static class WebLinkClassifier
{
    internal static bool IsExternalLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var value = href.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
            return true;

        // Application protocols such as vscode: and cursor: are not local paths.
        // Keep file URIs subject to the local-target checks.
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;
    }
}
