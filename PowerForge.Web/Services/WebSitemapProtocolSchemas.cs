using System.Text;

namespace PowerForge.Web;

/// <summary>Provides bundled sitemap.org protocol schemas for offline validation.</summary>
public static class WebSitemapProtocolSchemas
{
    /// <summary>Default file name for the bundled sitemap URL-set schema.</summary>
    public const string SitemapSchemaFileName = "sitemap.xsd";

    /// <summary>Default file name for the bundled sitemap index schema.</summary>
    public const string SitemapIndexSchemaFileName = "siteindex.xsd";

    /// <summary>Gets the bundled sitemap.xsd content.</summary>
    public static string GetSitemapSchema() => Protocols.Sitemaps.SitemapSchemas.GetSitemapSchema();
    /// <summary>Gets the bundled siteindex.xsd content.</summary>
    public static string GetSitemapIndexSchema() => Protocols.Sitemaps.SitemapSchemas.GetSitemapIndexSchema();
    /// <summary>Gets validation compatibility notes.</summary>
    public static string[] GetCompatibilityNotes() => Protocols.Sitemaps.SitemapSchemas.GetCompatibilityNotes();

    /// <summary>Writes the bundled schemas to <paramref name="outputDirectory"/>.</summary>
    public static WebSitemapProtocolSchemaExportResult ExportToDirectory(string outputDirectory, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);

        var sitemapPath = Path.Combine(directory, SitemapSchemaFileName);
        var sitemapIndexPath = Path.Combine(directory, SitemapIndexSchemaFileName);
        WriteIfNeeded(sitemapPath, GetSitemapSchema(), overwrite);
        WriteIfNeeded(sitemapIndexPath, GetSitemapIndexSchema(), overwrite);

        return new WebSitemapProtocolSchemaExportResult
        {
            OutputDirectory = directory,
            SitemapSchemaPath = sitemapPath,
            SitemapIndexSchemaPath = sitemapIndexPath,
            Notes = GetCompatibilityNotes()
        };
    }

    private static void WriteIfNeeded(string path, string content, bool overwrite)
    {
        if (!overwrite && File.Exists(path))
            return;

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

}
