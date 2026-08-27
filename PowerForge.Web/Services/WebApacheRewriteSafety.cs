namespace PowerForge.Web;

/// <summary>Provides shared safety rules for generated Apache rewrite artifacts.</summary>
public static class WebApacheRewriteSafety
{
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
