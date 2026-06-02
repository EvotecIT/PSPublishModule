using System.Text;

namespace PowerForge.Web;

/// <summary>Provides bundled sitemap.org protocol schemas for offline validation.</summary>
public static class WebSitemapProtocolSchemas
{
    /// <summary>Default file name for the bundled sitemap URL-set schema.</summary>
    public const string SitemapSchemaFileName = "sitemap.xsd";

    /// <summary>Default file name for the bundled sitemap index schema.</summary>
    public const string SitemapIndexSchemaFileName = "siteindex.xsd";

    private const string SitemapSchemaResourceName = "PowerForge.Web.Assets.SitemapProtocol.sitemap.xsd";
    private const string SitemapIndexSchemaResourceName = "PowerForge.Web.Assets.SitemapProtocol.siteindex.xsd";

    private static readonly string[] SchemaNotes =
    {
        "These files mirror the published sitemaps.org XSDs for strict offline schema validation.",
        "They do not allow PowerForge-generated XHTML language alternate links or Google extension elements.",
        "Pair them with content checks for protocol rules that the published XSDs do not fully encode."
    };

    /// <summary>Gets the bundled sitemap.xsd content.</summary>
    public static string GetSitemapSchema()
        => ReadResourceText(SitemapSchemaResourceName);

    /// <summary>Gets the bundled siteindex.xsd content.</summary>
    public static string GetSitemapIndexSchema()
        => ReadResourceText(SitemapIndexSchemaResourceName);

    /// <summary>Gets compatibility notes for consumers using the bundled schemas.</summary>
    public static string[] GetCompatibilityNotes()
        => (string[])SchemaNotes.Clone();

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

    private static string ReadResourceText(string resourceName)
    {
        var assembly = typeof(WebSitemapProtocolSchemas).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded sitemap schema resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
