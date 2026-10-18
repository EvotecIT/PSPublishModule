using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Provides shared safety rules for generated Apache rewrite artifacts.</summary>
public static class WebApacheRewriteSafety
{
    /// <summary>Escapes a decoded URL path for an Apache configuration pattern.</summary>
    /// <param name="path">The decoded path to match literally.</param>
    public static string EscapePathPattern(string path) =>
        Regex.Escape(path).Replace("\\ ", "\\x20", StringComparison.Ordinal)
            .Replace("\"", "\\x22", StringComparison.Ordinal).Replace("'", "\\x27", StringComparison.Ordinal);

    /// <summary>Escapes a literal redirect URL without introducing rewrite backreferences.</summary>
    /// <param name="destination">The literal, URI-encoded destination URL.</param>
    public static string EscapeLiteralDestination(string destination) =>
        destination.Replace("%", @"\%", StringComparison.Ordinal).Replace("$", @"\$", StringComparison.Ordinal);

    /// <summary>
    /// Excludes an already canonical request from an exact-path redirect, retaining
    /// authority normalization and any required query removal.
    /// </summary>
    /// <param name="lines">Configuration lines receiving the conditions.</param>
    /// <param name="sourcePath">The exact source path.</param>
    /// <param name="destination">The final destination after language-root normalization.</param>
    /// <param name="discardQuery">Whether this redirect discards an existing query string.</param>
    public static void AppendCanonicalPathCondition(ICollection<string> lines, string sourcePath, string destination, bool discardQuery)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!TryGetCanonicalPath(sourcePath, destination, out var path, out var absoluteTarget))
            return;

        var decodedPath = Uri.UnescapeDataString(path);
        var pathPattern = path.Equals(decodedPath, StringComparison.Ordinal)
            ? EscapePathPattern(path)
            : $"(?:{EscapePathPattern(path)}|{EscapePathPattern(decodedPath)})";
        lines.Add($"RewriteCond %{{REQUEST_URI}} !^{pathPattern}$" + (absoluteTarget is not null || discardQuery ? " [OR]" : string.Empty));
        if (absoluteTarget is not null)
        {
            var port = absoluteTarget.Scheme == Uri.UriSchemeHttps ? 443 : 80;
            lines.Add($"RewriteCond %{{HTTP_HOST}} !^{Regex.Escape(absoluteTarget.Host)}(?::{port})?$ [NC,OR]");
            lines.Add($"RewriteCond %{{HTTPS}} !^{(absoluteTarget.Scheme == Uri.UriSchemeHttps ? "on" : "off")}$" +
                (discardQuery ? " [OR]" : string.Empty));
        }
        if (discardQuery)
            lines.Add("RewriteCond %{QUERY_STRING} !^$");
    }

    internal static bool TryGetCanonicalPath(string sourcePath, string destination, out string path, out Uri? absoluteTarget)
    {
        path = destination;
        absoluteTarget = null;
        if (Uri.TryCreate(destination, UriKind.Absolute, out var target) &&
            (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps))
        {
            if (!target.IsDefaultPort || !string.IsNullOrEmpty(target.Query))
                return false;
            path = target.AbsolutePath;
            absoluteTarget = target;
        }
        else if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('?'))
            return false;

        var source = Uri.UnescapeDataString(WebLinkService.NormalizeSourcePath(sourcePath));
        var canonical = Uri.UnescapeDataString(WebLinkService.NormalizeSourcePath(path));
        if (!source.Equals(canonical, StringComparison.Ordinal))
            return false;

        if (absoluteTarget is null)
        {
            if (!Uri.TryCreate(new Uri("https://canonical.invalid"), path, out var relativeTarget))
                return false;
            path = relativeTarget.AbsolutePath;
        }
        return true;
    }

    /// <summary>
    /// Appends a condition that keeps certificate issuance and PowerForge deployment
    /// verification outside the next generated redirect rule without terminating
    /// unrelated rules in a containing virtual host.
    /// </summary>
    /// <param name="lines">The Apache configuration lines to append to.</param>
    public static void AppendOperationalPathCondition(ICollection<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        lines.Add("RewriteCond %{REQUEST_URI} !^/(?:\\.well-known/acme-challenge(?:/|$)|_powerforge/deployment\\.json$) [NC]");
    }
}
