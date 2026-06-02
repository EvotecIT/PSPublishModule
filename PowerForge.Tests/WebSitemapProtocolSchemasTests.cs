using System.Xml;
using System.Xml.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebSitemapProtocolSchemasTests
{
    [Fact]
    public void BundledSchemas_LoadAndCompile()
    {
        CompileSchema(WebSitemapProtocolSchemas.GetSitemapSchema());
        CompileSchema(WebSitemapProtocolSchemas.GetSitemapIndexSchema());

        Assert.Contains("urlset", WebSitemapProtocolSchemas.GetSitemapSchema(), StringComparison.Ordinal);
        Assert.Contains("sitemapindex", WebSitemapProtocolSchemas.GetSitemapIndexSchema(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportToDirectory_WritesProtocolSchemas()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-schemas-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSitemapProtocolSchemas.ExportToDirectory(root);

            Assert.True(File.Exists(result.SitemapSchemaPath));
            Assert.True(File.Exists(result.SitemapIndexSchemaPath));
            Assert.Equal(root, result.OutputDirectory);
            Assert.Contains(result.Notes, note => note.Contains("published sitemaps.org XSDs", StringComparison.Ordinal));
            Assert.Contains(result.Notes, note => note.Contains("XHTML language alternate", StringComparison.Ordinal));
            Assert.Contains("XML Schema for Sitemap files", File.ReadAllText(result.SitemapSchemaPath), StringComparison.Ordinal);
            Assert.Contains("XML Schema for Sitemap index files", File.ReadAllText(result.SitemapIndexSchemaPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_SitemapSchemas_ExportsProtocolSchemas()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-schemas-cli-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "sitemap-schemas",
                new[] { "--out", root },
                outputJson: false,
                new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(root, WebSitemapProtocolSchemas.SitemapSchemaFileName)));
            Assert.True(File.Exists(Path.Combine(root, WebSitemapProtocolSchemas.SitemapIndexSchemaFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CompileSchema(string schemaText)
    {
        var schemas = new XmlSchemaSet();
        using var stringReader = new StringReader(schemaText);
        using var xmlReader = XmlReader.Create(stringReader);
        schemas.Add(null, xmlReader);
        schemas.Compile();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
