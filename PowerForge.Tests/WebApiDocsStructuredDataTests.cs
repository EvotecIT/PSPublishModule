using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsStructuredDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-api-schema-" + Guid.NewGuid().ToString("N"));

    public WebApiDocsStructuredDataTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("docs", false)]
    [InlineData("simple", false)]
    [InlineData("docs", true)]
    [InlineData("simple", true)]
    public void Generate_InheritsSiteSchemaOnIndexAndCanonicalReferencePages(string template, bool powerShell)
    {
        var options = Options(template, powerShell);
        WebApiDocsGenerator.Generate(options);

        var index = Models(File.ReadAllText(Path.Combine(options.OutputPath, "index.html")));
        AssertPage(index, "https://product.example/api/", "Reference </script><script>alert(1)</script>");
        Assert.Equal(2, OfType(index, "BreadcrumbList").GetProperty("itemListElement").GetArrayLength());

        var typeFile = template == "docs"
            ? Directory.GetFiles(options.OutputPath, "index.html", SearchOption.AllDirectories).Single(p => Path.GetDirectoryName(p) != options.OutputPath)
            : Assert.Single(Directory.GetFiles(Path.Combine(options.OutputPath, "types"), "*.html"));
        var html = File.ReadAllText(typeFile);
        var canonical = Regex.Match(html, "<link rel=\"canonical\" href=\"([^\"]+)\"").Groups[1].Value;
        var models = Models(html);
        AssertPage(models, canonical, null);
        var crumbs = OfType(models, "BreadcrumbList").GetProperty("itemListElement");
        Assert.Equal(3, crumbs.GetArrayLength());
        Assert.Equal(canonical, crumbs[2].GetProperty("item").GetString());
        Assert.Contains(powerShell ? "New-Sample" : "Widget", OfType(models, "WebPage").GetProperty("name").GetString());
        Assert.DoesNotContain("</script><script>alert(1)</script>", Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);
    }

    [Fact]
    public void Generate_SiteConfigAloneProvidesPublicOriginAndIdentity()
    {
        var options = Options("docs", false);
        options.NavJsonPath = null;
        options.SiteBaseUrl = null;
        WebApiDocsGenerator.Generate(options);
        var html = File.ReadAllText(Path.Combine(options.OutputPath, "index.html"));
        Assert.Contains("rel=\"canonical\" href=\"https://product.example/api/\"", html);
        AssertPage(Models(html), "https://product.example/api/", options.Title);
    }

    [Fact]
    public void Generate_UsesMergedSiteOriginWithPermissiveJsonAndSeparateNavigation()
    {
        var options = Options("docs", false);
        File.Copy(options.SiteConfigPath!, Path.Combine(_root, "base.json"));
        File.WriteAllText(options.SiteConfigPath!, """
            {
              // Site configuration accepts comments, inheritance and trailing commas.
              "extends": "./base.json",
            }
            """);
        options.NavJsonPath = Path.Combine(_root, "nav.json");
        File.WriteAllText(options.NavJsonPath, """{"navigation": {"menus": []}}""");
        options.SiteBaseUrl = null;
        WebApiDocsGenerator.Generate(options);
        var html = File.ReadAllText(Path.Combine(options.OutputPath, "index.html"));
        Assert.Contains("rel=\"canonical\" href=\"https://product.example/api/\"", html);
        AssertPage(Models(html), "https://product.example/api/", options.Title);

        options.NavJsonPath = null;
        WebApiDocsGenerator.Generate(options);
        html = File.ReadAllText(Path.Combine(options.OutputPath, "index.html"));
        AssertPage(Models(html), "https://product.example/api/", options.Title);
    }

    [Fact]
    public void Generate_SiteConfigFallbackRetainsCamelCaseHeadLinks()
    {
        var options = Options("docs", false);
        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(options.SiteConfigPath!))!;
        config["head"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"links":[{"rel":"icon","href":"/brand.svg","type":"image/svg+xml"}]}
            """);
        File.WriteAllText(options.SiteConfigPath!, config.ToJsonString());
        options.NavJsonPath = null;
        WebApiDocsGenerator.Generate(options);
        var html = File.ReadAllText(Path.Combine(options.OutputPath, "index.html"));
        Assert.Contains("rel=\"icon\" href=\"/brand.svg\"", html);
    }

    [Fact]
    public void GenerateSuitePortal_InheritsLinkedSiteSchema()
    {
        var options = Options("docs", false);
        options.ApiSuiteEntries.Add(new WebApiDocsSuiteEntry { Id = "a", Label = "A", Href = "/api/a/" });
        options.ApiSuiteEntries.Add(new WebApiDocsSuiteEntry { Id = "b", Label = "B", Href = "/api/b/" });
        WebApiDocsGenerator.GenerateSuitePortal(options);
        var models = Models(File.ReadAllText(Path.Combine(options.OutputPath, "index.html")));
        AssertPage(models, "https://product.example/api/", options.Title);
    }

    [Theory]
    [InlineData(false, "https://product.example")]
    [InlineData(true, "")]
    public void Generate_DoesNotInventSchemaWhenDisabledOrWithoutPublicOrigin(bool enabled, string origin)
    {
        var options = Options("docs", false, enabled, origin);
        WebApiDocsGenerator.Generate(options);
        foreach (var path in Directory.GetFiles(options.OutputPath, "*.html", SearchOption.AllDirectories))
            Assert.DoesNotContain("application/ld+json", File.ReadAllText(path));
    }

    private WebApiDocsOptions Options(string template, bool powerShell, bool enabled = true, string origin = "https://product.example")
    {
        var config = Path.Combine(_root, "site.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            Name = "Example Product", BaseUrl = origin,
            StructuredData = new { Enabled = enabled, WebPage = true, Breadcrumbs = true,
                OrganizationName = "Example Publisher", OrganizationUrl = "https://publisher.example" }
        }));
        var source = Path.Combine(_root, "api.xml");
        File.WriteAllText(source, powerShell
            ? """
              <helpItems xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                <command:command><command:details><command:name>New-Sample</command:name>
                  <maml:description><maml:para>Creates a sample.</maml:para></maml:description>
                </command:details></command:command>
              </helpItems>
              """
            : """
              <doc><assembly><name>Sample</name></assembly><members>
                <member name="T:Sample.Widget"><summary>A documented widget.</summary></member>
              </members></doc>
              """);
        return new WebApiDocsOptions
        {
            Type = powerShell ? ApiDocsType.PowerShell : ApiDocsType.CSharp,
            XmlPath = source, HelpPath = powerShell ? source : null,
            OutputPath = Path.Combine(_root, "output"), BaseUrl = "/api", SiteBaseUrl = origin,
            SiteConfigPath = config, NavJsonPath = config, Title = "Reference </script><script>alert(1)</script>",
            Format = "html", Template = template, InjectPrismAssets = false
        };
    }

    private static void AssertPage(JsonElement[] models, string url, string? name)
    {
        var page = OfType(models, "WebPage");
        Assert.Equal(url, page.GetProperty("url").GetString());
        Assert.Equal(url + "#webpage", page.GetProperty("@id").GetString());
        if (name is not null)
            Assert.Equal(name, page.GetProperty("name").GetString());
        Assert.Equal("https://product.example/#website", page.GetProperty("isPartOf").GetProperty("@id").GetString());
        var publisher = OfType(models, "Organization");
        Assert.Equal("Example Publisher", publisher.GetProperty("name").GetString());
        Assert.Equal("https://publisher.example/#organization", publisher.GetProperty("@id").GetString());
        Assert.Equal(publisher.GetProperty("@id").GetString(), OfType(models, "WebSite").GetProperty("publisher").GetProperty("@id").GetString());
    }

    private static JsonElement[] Models(string html) => Regex.Matches(html,
        "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
        .Select(m => { using var document = JsonDocument.Parse(m.Groups[1].Value); return document.RootElement.Clone(); }).ToArray();

    private static JsonElement OfType(JsonElement[] models, string type) =>
        Assert.Single(models, m => m.GetProperty("@type").GetString() == type);

    public void Dispose() => Directory.Delete(_root, true);
}
