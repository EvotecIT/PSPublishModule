using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSeoDoctorJsonLdGraphTests
{
    [Theory]
    [InlineData("graph")]
    [InlineData("graph-in-array")]
    [InlineData("nested-graph")]
    [InlineData("type-array")]
    public void Analyze_AcceptsTypesDeclaredInsideJsonLdGraph(string shape)
    {
        const string graph = """{"@context":"https://schema.org","@graph":[{"@type":"Organization","name":"Example"},{"@type":"WebSite","name":"Example","url":"https://example.com/"}]}""";
        var payload = shape switch
        {
            "graph-in-array" => $"[{graph}]",
            "nested-graph" => """{"@context":"https://schema.org","@graph":[{"@graph":[{"@type":"Organization","name":"Example"}]}]}""",
            "type-array" => """{"@context":"https://schema.org","@graph":[{"@type":["Organization","Corporation"],"name":"Example"}]}""",
            _ => graph
        };

        var result = Analyze(payload);

        Assert.DoesNotContain(result.Issues, issue => issue.Category == "structured-data");
        Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("untyped")]
    public void Analyze_StillFlagsGraphsWithoutAnyDeclaredType(string shape)
    {
        var payload = shape == "empty"
            ? """{"@context":"https://schema.org","@graph":[]}"""
            : """{"@context":"https://schema.org","@graph":[{"name":"Example"}]}""";

        var result = Analyze(payload);

        Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-missing-type");
    }

    [Fact]
    public void Analyze_StillChecksEntityProfilesInsideJsonLdGraph()
    {
        var result = Analyze("""{"@context":"https://schema.org","@graph":[{"@type":"BreadcrumbList","itemListElement":[{"@type":"ListItem","position":1,"name":"Home"}]}]}""");

        Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-breadcrumb-items");
        Assert.DoesNotContain(result.Issues, issue => issue.Hint == "structured-data-missing-type");
    }

    [Theory]
    [InlineData("graph-boolean")]
    [InlineData("graph-object-array")]
    [InlineData("standalone-boolean")]
    public void Analyze_DoesNotTreatNonStringValuesAsDeclaredTypes(string shape)
    {
        var payload = shape switch
        {
            "graph-object-array" => """{"@context":"https://schema.org","@graph":[{"@type":[{"name":"Organization"}]}]}""",
            "standalone-boolean" => """{"@context":"https://schema.org","@type":true}""",
            _ => """{"@context":"https://schema.org","@graph":[{"@type":true}]}"""
        };

        var result = Analyze(payload);

        Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-missing-type");
    }

    private static WebSeoDoctorResult Analyze(string payload)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-jsonld-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                $"<!doctype html><html><head><script type=\"application/ld+json\">{payload}</script></head><body></body></html>");
            return WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckContentLeaks = false,
                CheckStructuredData = true,
                RequireStructuredData = true
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
