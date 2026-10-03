using System.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorSidebarSizeTests
{
    [Fact]
    public void GenerateDocsHtml_LargeNamespaceKeepsCompleteIndexAndLoadsTypeCatalogOnDemand()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sidebar-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            var xml = new StringBuilder("<doc><assembly><name>Test</name></assembly><members>");
            foreach (var namespaceName in new[] { "Alpha", "Beta" })
            {
                for (var number = 0; number < 101; number++)
                    xml.Append($"<member name=\"T:{namespaceName}.A{number:000}\"><summary>Type {number}.</summary></member>");
            }
            xml.Append("<member name=\"T:Beta.IContract\"><summary>Mixed kind.</summary></member>");
            xml.Append("</members></doc>");
            File.WriteAllText(xmlPath, xml.ToString());

            var outputPath = Path.Combine(root, "api");
            var options = new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Format = "html",
                Template = "docs",
                BaseUrl = "/api"
            };
            options.QuickStartTypeNames.Add("A000");

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(203, result.TypeCount);

            var indexSidebar = ReadSidebarNavigation(Path.Combine(outputPath, "index.html"));
            var alphaPage = Path.Combine(outputPath, "alpha-a001", "index.html");
            var alphaSidebar = ReadSidebarNavigation(alphaPage);
            Assert.Contains("href=\"/api/beta-a001/\"", indexSidebar, StringComparison.Ordinal);
            Assert.Contains("href=\"/api/alpha-a002/\"", alphaSidebar, StringComparison.Ordinal);
            Assert.Contains("href=\"/api/alpha-a001/\" class=\"type-item active\"", alphaSidebar, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/api/alpha-a100/\"", alphaSidebar, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/api/beta-a001/\"", alphaSidebar, StringComparison.Ordinal);
            Assert.Contains("href=\"/api/\">Browse all 203 types</a>", File.ReadAllText(alphaPage), StringComparison.Ordinal);
            Assert.Contains("data-type-catalog=\"/api/index.json\"", File.ReadAllText(alphaPage), StringComparison.Ordinal);

            var betaSidebar = ReadSidebarNavigation(Path.Combine(outputPath, "beta-a000", "index.html"));
            Assert.Contains("href=\"/api/beta-a000/\" class=\"type-item active\"", betaSidebar, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadSidebarNavigation(string path)
    {
        var html = File.ReadAllText(path);
        const string open = "<nav class=\"sidebar-nav\">";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing sidebar navigation in {path}.");
        var end = html.IndexOf("</nav>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Unclosed sidebar navigation in {path}.");
        return html.Substring(start, end + "</nav>".Length - start);
    }
}
