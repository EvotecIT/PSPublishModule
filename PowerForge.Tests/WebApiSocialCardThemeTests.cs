using PowerForge.Web;
using PowerForge.Web.Cli;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using ImageMagick;

namespace PowerForge.Tests;

public sealed partial class WebApiSocialCardThemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-api-card-theme-" + Guid.NewGuid().ToString("N"));
    public WebApiSocialCardThemeTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("docs", false, false, false, false)]
    [InlineData("simple", false, false, false, false)]
    [InlineData("docs", true, false, false, false)]
    [InlineData("docs", false, true, false, false)]
    [InlineData("docs", false, false, true, true)]
    [InlineData("docs", true, false, true, true)]
    public void ApiCards_UseNamedThemeAndSelectedTitleSource_KeepEffectiveHostAndSeoMetadata(
        string template, bool powerShell, bool collectionTitle, bool seoTitle, bool alternateHost)
    {
        if (!WebSocialCardGenerator.IsPngRenderingAvailable())
            return;
        var spec = new SiteSpec
        {
            Name = "Example", BaseUrl = "https://example.test",
            Social = new SocialSpec
            {
                AutoGenerateCards = true, GeneratedCardTextFit = true,
                GeneratedCardThemes = new() { ["quiet-api"] = new SocialCardThemeSpec
                {
                    Style = "api", Variant = "code-led", Tokens = new() { ["socialCard"] = new Dictionary<string, object?> { ["accent"] = "#123456", ["accentSoft"] = "#abcdef" } }
                } }
            }
        };
        var config = Path.Combine(_root, "site.json");
        if (collectionTitle)
            spec.Social.GeneratedCardTitlesByCollection = new() { ["api"] = "page" };
        File.WriteAllText(config, JsonSerializer.Serialize(spec));
        var xmlPath = Path.Combine(_root, "api.xml");
        File.WriteAllText(xmlPath, collectionTitle
            ? "<doc><assembly><name>Demo</name></assembly><members><member name=\"T:Demo.Terminal`1\"><summary>Build readable output.</summary><typeparam name=\"T\">Output value.</typeparam></member></members></doc>"
            : "<doc><assembly><name>Demo</name></assembly><members><member name=\"T:Demo.Terminal\"><summary>Build readable output.</summary></member></members></doc>");
        var helpPath = Path.Combine(_root, "help.xml");
        File.WriteAllText(helpPath, """
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
              <command:command>
                <command:details><command:name>Format-ConsoleTable</command:name><maml:description><maml:para>Build readable output.</maml:para></maml:description></command:details>
                <command:syntax><command:syntaxItem><command:name>Format-ConsoleTable</command:name><command:parameter required="true" position="0"><maml:name>InputObject</maml:name><command:parameterValue required="true">psobject</command:parameterValue></command:parameter></command:syntaxItem></command:syntax>
              </command:command>
            </helpItems>
            """);
        var outPath = Path.Combine(_root, "_site", "api");
        var options = new WebApiDocsOptions
        {
            Type = powerShell ? ApiDocsType.PowerShell : ApiDocsType.CSharp,
            XmlPath = xmlPath, HelpPath = helpPath, OutputPath = outPath, SiteConfigPath = config,
            Title = "Example API reference", BaseUrl = "/api", SiteName = "Example",
            Template = template, Format = "both", AutoGenerateSocialCards = true,
            SocialCardTheme = "quiet-api", SocialCardTitle = seoTitle ? "seo" : collectionTitle ? null : "name", SocialCardsRequired = true,
            SiteBaseUrl = alternateHost ? "https://preview.example.test" : null
        };
        var result = WebApiDocsGenerator.Generate(options);
        Assert.Equal(1, result.TypeCount);
        var pagePath = Directory.EnumerateFiles(outPath, "*.html", SearchOption.AllDirectories)
            .Single(path => !string.Equals(path, Path.Combine(outPath, "index.html"), StringComparison.OrdinalIgnoreCase) &&
                (template == "docs" ? Path.GetFileName(path) == "index.html" : Path.GetDirectoryName(path) == Path.Combine(outPath, "types")));
        var html = File.ReadAllText(pagePath);
        string Meta(string name) => WebUtility.HtmlDecode(Regex.Match(html, "property=\"" + name + "\" content=\"([^\"]*)\"").Groups[1].Value);
        Assert.EndsWith(" - Example API reference", Meta("og:title"));
        var route = new Uri(Meta("og:url")).AbsolutePath;
        var image = new Uri(Meta("og:image")).AbsolutePath;
        Assert.Equal(alternateHost ? "preview.example.test" : "example.test", new Uri(Meta("og:url")).Host);
        var typeJson = Directory.EnumerateFiles(Path.Combine(outPath, "types"), "*.json").Single();
        using var typeModel = JsonDocument.Parse(File.ReadAllText(typeJson));
        var code = powerShell ? typeModel.RootElement.GetProperty("methods")[0].GetProperty("signature").GetString()! : collectionTitle ? "public class Terminal<T>" : "public class Terminal";
        Assert.Contains(powerShell ? "InputObject" : "Terminal", code, StringComparison.Ordinal);
        spec.Social.GeneratedCardTheme = "quiet-api";
        spec.Social.GeneratedCardsPath = "/assets/social/generated/api";
        spec.BaseUrl = options.SiteBaseUrl ?? spec.BaseUrl;
        var item = new ContentItem
        {
            Title = powerShell ? "Format-ConsoleTable" : "Terminal", Description = "Build readable output.", OutputPath = route, Collection = powerShell ? "powershell" : "api",
            Meta = new() { ["social_title"] = seoTitle ? Meta("og:title") : powerShell ? "Format-ConsoleTable" : "Terminal", ["social_card_badge"] = powerShell ? "POWERSHELL" : "API", ["social_card_code"] = code }
        };
        var expected = WebSiteBuilder.CreateSocialCardGenerationPlan(spec, item, Meta("og:title"), Meta("og:description"), "Example", _root)!;
        Assert.Equal(powerShell ? "POWERSHELL" : "API", expected.RenderOptions.Badge);
        Assert.Equal(alternateHost ? "preview.example.test" : "example.test", expected.RenderOptions.SiteAddress);
        Assert.Equal("Build readable output.", expected.RenderOptions.Description);
        Assert.Equal("/" + expected.RelativePath, image);
        using var actualPng = new MagickImage(Path.Combine(_root, "_site", image.TrimStart('/')));
        using var expectedPng = new MagickImage(WebSocialCardGenerator.RenderPng(expected.RenderOptions)!);
        Assert.Equal(expectedPng.GetPixels().ToByteArray(PixelMapping.RGBA), actualPng.GetPixels().ToByteArray(PixelMapping.RGBA));
    }

    [Fact]
    public void PipelineApiCards_RejectUnknownTheme_InsteadOfSilentlyUsingReferenceLayout()
    {
        File.WriteAllText(Path.Combine(_root, "site.json"), """{"Name":"Example","Social":{"AutoGenerateCards":true}}""");
        File.WriteAllText(Path.Combine(_root, "api.xml"), "<doc><assembly><name>Demo</name></assembly><members/></doc>");
        var pipeline = Path.Combine(_root, "pipeline.json");
        File.WriteAllText(pipeline, """
            {"steps":[{"task":"apidocs","config":"site.json","xml":"api.xml","out":"_site/api","format":"html","socialAutoGenerate":true,"socialCardTheme":"missing","socialCardTitle":"name","socialCardsRequired":true}]}
            """);
        var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
        Assert.False(result.Success);
        Assert.Contains("theme 'missing'", result.Steps.Single().Message, StringComparison.Ordinal);
    }
}
