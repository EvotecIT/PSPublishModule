using System.Net;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiDocsIndexIntroTests
{
    [Theory]
    [InlineData("docs", false)]
    [InlineData("simple", true)]
    public void Pipeline_UsesIndexCopyAndPreservesPreRenderedChrome(string template, bool dashedOptions)
    {
        WithRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "docs.xml"), "<doc><members><member name=\"T:Charts.Chart\"><summary>A chart.</summary></member></members></doc>");
            File.WriteAllText(Path.Combine(root, "header.html"), "<header><nav><a href=\"/docs/\">Guide</a></nav></header>");
            File.WriteAllText(Path.Combine(root, "footer.html"), "<footer>Rendered footer</footer>");
            File.WriteAllText(Path.Combine(root, "site.json"), "{\"Name\":\"Charts\",\"BaseUrl\":\"https://example.test\"}");
            var lead = "Build <charts> & explore APIs.";
            var description = "Chart reference & examples for generic <T> values.";
            var step = new Dictionary<string, object?>
            {
                ["task"] = "apidocs", ["xml"] = "docs.xml", ["out"] = "api", ["format"] = "html", ["template"] = template,
                ["headerHtml"] = "header.html", ["footerHtml"] = "footer.html", ["nav"] = "site.json",
                [dashedOptions ? "index-lead" : "indexLead"] = lead,
                [dashedOptions ? "index-description" : "indexDescription"] = description,
                ["failOnWarnings"] = false
            };
            File.WriteAllText(Path.Combine(root, "pipeline.json"), JsonSerializer.Serialize(new { steps = new[] { step } }));
            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);
            Assert.True(result.Success, Assert.Single(result.Steps).Message);
            Assert.DoesNotContain("nav injection may be empty", Assert.Single(result.Steps).Message);
            var html = File.ReadAllText(Path.Combine(root, "api", "index.html"));
            Assert.Contains($"<p class=\"lead\">{WebUtility.HtmlEncode(lead)}</p>", html);
            Assert.Contains($"<meta name=\"description\" content=\"{WebUtility.HtmlEncode(description)}\"", html);
            Assert.Contains($"<meta property=\"og:description\" content=\"{WebUtility.HtmlEncode(description)}\"", html);
            Assert.Contains("<a href=\"/docs/\">Guide</a>", html);
            Assert.Contains("<footer>Rendered footer</footer>", html);
        });
    }

    [Theory]
    [InlineData("inputs", false)]
    [InlineData("inputs", true)]
    [InlineData("entries", false)]
    [InlineData("entries", true)]
    public void Pipeline_BatchChildCopyOverridesParentAcrossAliases(string batchKey, bool dashedParent)
    {
        WithRoot(root =>
        {
            File.WriteAllText(Path.Combine(root, "docs.xml"), "<doc><members><member name=\"T:Charts.Chart\"><summary>A chart.</summary></member></members></doc>");
            var parent = new Dictionary<string, object?>
            {
                ["task"] = "apidocs", ["xml"] = "docs.xml", ["format"] = "html", ["template"] = "docs",
                [dashedParent ? "index-lead" : "indexLead"] = "Shared lead",
                [dashedParent ? "index-description" : "indexDescription"] = "Shared description",
                ["failOnWarnings"] = false,
                [batchKey] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["out"] = "custom",
                        [dashedParent ? "indexLead" : "index-lead"] = "Child lead",
                        [dashedParent ? "indexDescription" : "index-description"] = "Child description"
                    },
                    new Dictionary<string, object?> { ["out"] = "shared" }
                }
            };
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new { steps = new[] { parent } }));
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success, Assert.Single(result.Steps).Message);
            foreach (var (directory, prefix) in new[] { ("custom", "Child"), ("shared", "Shared") })
            {
                var html = File.ReadAllText(Path.Combine(root, directory, "index.html"));
                Assert.Contains($"<p class=\"lead\">{prefix} lead</p>", html);
                Assert.Contains($"<meta name=\"description\" content=\"{prefix} description\"", html);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_PowerShellIndexUsesHelpNameOrDiscoveredManifestDescription(bool manifest)
    {
        WithRoot(root =>
        {
            var help = Path.Combine(root, "ImagePlayground-help.xml");
            File.WriteAllText(help, """
                <helpItems xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                  <command:command><command:details><command:name>New-Chart</command:name>
                  <maml:description><maml:para>Creates a chart.</maml:para></maml:description></command:details></command:command>
                </helpItems>
                """);
            if (manifest)
                File.WriteAllText(Path.Combine(root, "ImagePlayground.psd1"), "@{ ModuleVersion = '1.0.0'; Description = 'Creates charts and images from PowerShell.' }");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell, HelpPath = help, OutputPath = Path.Combine(root, "api"), Format = "html", Template = "docs"
            });
            var html = File.ReadAllText(Path.Combine(root, "api", "index.html"));
            Assert.Contains(manifest ? "Creates charts and images from PowerShell." : "PowerShell command reference for ImagePlayground", html);
            Assert.DoesNotContain("Complete API documentation auto-generated", html);
        });
    }

    [Fact]
    public void Generate_MultiAssemblyIndexUsesAssemblyDescription()
    {
        WithRoot(root =>
        {
            var assembly = typeof(WebApiDocsGenerator).Assembly.Location;
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><assembly><name>PowerForge.Web</name></assembly><members><member name=\"T:PowerForge.Web.WebApiDocsOptions\"><summary>Options.</summary></member></members></doc>");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPaths = new[] { assembly }, OutputPath = Path.Combine(root, "api"),
                Format = "html", Template = "docs", IncludeUndocumentedTypes = false
            });
            Assert.Contains("PowerForge.Web static-site engine and reusable website build components.", File.ReadAllText(Path.Combine(root, "api", "index.html")));
        });
    }

    [Fact]
    public void Generate_IndexWithoutPackageMetadataUsesProductTitle()
    {
        WithRoot(root =>
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><members><member name=\"T:Charts.Chart\"><summary>A chart.</summary></member></members></doc>");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, Title = "ChartForgeX API reference", OutputPath = Path.Combine(root, "api"), Format = "html", Template = "docs"
            });
            Assert.Contains("API reference for ChartForgeX, with types, members, and signatures.", File.ReadAllText(Path.Combine(root, "api", "index.html")));
        });
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-intro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
