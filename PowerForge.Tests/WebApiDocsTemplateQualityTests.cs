using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebApiDocsTemplateQualityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pf-apidocs-template-" + Guid.NewGuid().ToString("N"));

    public WebApiDocsTemplateQualityTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("docs")]
    [InlineData("simple")]
    public void Generate_WithCustomStylesheet_EmitsSharedCssWithoutChangingReplacementContract(string template)
    {
        var xml = Path.Combine(root, "docs.xml");
        File.WriteAllText(xml, "<doc><members><member name=\"T:Demo.Sample\"><summary>Sample.</summary></member></members></doc>");
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions
        {
            XmlPath = xml, OutputPath = output, Template = template, Format = "html", CssHref = "/custom-api.css"
        });
        var css = File.ReadAllText(Path.Combine(output, "powerforge-api.css"));
        Assert.Contains("--pf-bg:", css);
        Assert.Contains("--pf-ink:", css);
        Assert.Contains("--pf-accent:", css);
        var html = File.ReadAllText(Path.Combine(output, "index.html"));
        Assert.Contains("href=\"/custom-api.css\"", html);
        Assert.DoesNotContain("href=\"powerforge-api.css\"", html);
        Assert.DoesNotContain("--pf-bg:", html);
    }

    [Theory]
    [InlineData("https://github.com/example/repo/blob/main/{path}#L{line}", "type-edit-action")]
    [InlineData("https://example.test/source/{path}#L{line}", "type-view-source-action")]
    public void Generate_DotNetReference_UsesSemanticFactsAndQuietHeader(string sourcePattern, string actionClass)
    {
        var html = GenerateDotNet(sourcePattern);
        var header = Regex.Match(html, "<header class=\"type-header.*?</header>", RegexOptions.Singleline).Value;
        Assert.Contains("<h1>WebTemplateQualityFixture</h1>", header);
        Assert.Contains("<p class=\"type-namespace\"><code>PowerForge.Tests</code></p>", header);
        Assert.DoesNotContain("ev-eyebrow", header);
        Assert.Contains($"type-source-action {actionClass}", header);
        var facts = XElement.Parse(Regex.Match(html, "<dl class=\"type-meta\">.*?</dl>", RegexOptions.Singleline).Value);
        Assert.NotEmpty(facts.Elements("div"));
        Assert.All(facts.Elements("div"), row =>
        {
            Assert.Single(row.Elements("dt"));
            Assert.Single(row.Elements("dd"));
            Assert.Equal("type-meta-label", row.Element("dt")!.Attribute("class")!.Value);
        });
        Assert.Contains("class=\"custom-body pf-api-docs pf-api-docs--dotnet pf-api-docs--suite-quality-suite\"", html);
    }

    [Fact]
    public void Generate_DotNetMembers_CombinesReturnsAndOmitsVoidAndPosition()
    {
        var html = GenerateDotNet();
        var cards = Regex.Split(html, "<div class=\"member-card\"").Skip(1).ToArray();
        var returning = Assert.Single(cards, card => card.StartsWith(" id=\"method-build-string\"", StringComparison.Ordinal));
        Assert.Single(Regex.Matches(returning, "class=\"member-returns\"").Cast<Match>());
        Assert.Contains("member-returns-type", returning);
        Assert.Contains("member-returns-text\">The built text.", returning);
        var noReturn = Assert.Single(cards, card => card.StartsWith(" id=\"method-clear\"", StringComparison.Ordinal));
        Assert.DoesNotContain("class=\"member-returns\"", noReturn);
        Assert.DoesNotMatch("<span class=\"param-meta-chip[^\"]*\">position:", html);
        Assert.Contains("<p class=\"member-subhead\">Parameters</p>", returning);
        Assert.DoesNotContain("<h3>Parameters</h3>", html);
        Assert.DoesNotContain("<h3>Returns</h3>", html);
    }

    [Fact]
    public void Generate_ReferenceControls_HaveNamesGroupsAndSectionRelationships()
    {
        var html = GenerateDotNet();
        Assert.Matches("<label class=\"sr-only\" for=\"api-filter\">Filter [^<]+</label>", html);
        Assert.Contains("id=\"api-filter\" type=\"search\"", html);
        Assert.Contains("class=\"sidebar-search-icon\" aria-hidden=\"true\"", html);
        Assert.Contains("<label class=\"sr-only\" for=\"api-member-filter\">Filter members</label>", html);
        Assert.Contains("id=\"api-member-filter\" type=\"search\"", html);
        Assert.Contains("class=\"member-kind-filter\" role=\"group\" aria-label=\"Member kinds\"", html);
        var status = html.IndexOf("class=\"sidebar-status\"", StringComparison.Ordinal);
        var count = html.IndexOf("class=\"sidebar-count\"", StringComparison.Ordinal);
        var tools = html.IndexOf("class=\"sidebar-tools\"", StringComparison.Ordinal);
        Assert.True(status >= 0 && count > status && tools > count);
        foreach (Match button in Regex.Matches(html, "<button class=\"(?:filter-button|member-kind)(?: active)?\"[^>]+>"))
            Assert.Contains(button.Value.Contains(" active\"", StringComparison.Ordinal) ? "aria-pressed=\"true\"" : "aria-pressed=\"false\"", button.Value);
        var toggles = Regex.Matches(html, "<button class=\"member-section-toggle\"[^>]+>");
        Assert.NotEmpty(toggles.Cast<Match>());
        var bodyIds = new HashSet<string>();
        foreach (Match toggle in toggles)
        {
            var bodyId = Regex.Match(toggle.Value, "aria-controls=\"([^\"]+)\"").Groups[1].Value;
            Assert.True(bodyIds.Add(bodyId), "Section body IDs must be unique.");
            var body = Regex.Match(html, "<div class=\"member-section-body\" id=\"" + Regex.Escape(bodyId) + "\"[^>]*>").Value;
            Assert.NotEmpty(body);
            Assert.Contains(body.Contains(" hidden", StringComparison.Ordinal) ? "aria-expanded=\"false\"" : "aria-expanded=\"true\"", toggle.Value);
        }
    }

    [Theory]
    [InlineData(false, "engine")]
    [InlineData(true, "engine")]
    [InlineData(false, "pipeline")]
    [InlineData(true, "pipeline")]
    [InlineData(false, "cli")]
    [InlineData(true, "cli")]
    public void Generate_PowerShellExamples_ProvenanceIsOptInThroughEveryEntryPoint(bool showOrigins, string entryPoint)
    {
        var help = Path.Combine(root, "Demo-help.xml");
        File.WriteAllText(help, HelpXml);
        var output = Path.Combine(root, "api");
        if (entryPoint == "engine")
        {
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell, HelpPath = help, OutputPath = output,
                Template = "docs", Format = "both", PowerShellModuleName = "Demo"
            };
            Assert.False(options.ShowExampleOrigins);
            if (showOrigins) options.ShowExampleOrigins = true;
            WebApiDocsGenerator.Generate(options);
        }
        else if (entryPoint == "pipeline")
        {
            var step = new Dictionary<string, object>
            {
                ["task"] = "apidocs", ["type"] = "PowerShell", ["help"] = help,
                ["out"] = output, ["template"] = "docs", ["format"] = "both"
            };
            if (showOrigins) step["showExampleOrigins"] = true;
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new { steps = new[] { step } }));
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
        }
        else
        {
            var args = new List<string> { "--type", "PowerShell", "--help-path", help, "--out", output, "--template", "docs", "--format", "both" };
            if (showOrigins) args.Add("--show-example-origins");
            Assert.Equal(0, WebCliCommandHandlers.HandleSubCommand("apidocs", args.ToArray(), outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
        }
        var html = File.ReadAllText(Path.Combine(output, "get-demo", "index.html"));
        Assert.Equal(showOrigins, html.Contains("<div class=\"example-origin\"", StringComparison.Ordinal));
        Assert.Contains("<h3 class=\"example-title\">Example 1</h3>", html);
        Assert.Contains("pf-api-docs--powershell", html);
        Assert.Contains("param-meta-chip param-meta-chip--position", html);
        Assert.Contains("param-meta-chip pipeline param-meta-chip--pipeline", html);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "get-demo.json")));
        Assert.Contains(json.RootElement.GetProperty("examples").EnumerateArray(), example => example.GetProperty("origin").GetString() == "AuthoredHelp");
    }

    private string GenerateDotNet(string sourcePattern = "https://github.com/example/repo/blob/main/{path}#L{line}")
    {
        var xml = Path.Combine(root, "docs.xml");
        File.WriteAllText(xml, """
            <doc><assembly><name>PowerForge.Tests</name></assembly><members>
              <member name="T:PowerForge.Tests.WebTemplateQualityFixture"><summary>Template fixture.</summary></member>
              <member name="M:PowerForge.Tests.WebTemplateQualityFixture.Build(System.String)"><summary>Builds text.</summary><param name="name">The name.</param><returns>The built text.</returns></member>
              <member name="M:PowerForge.Tests.WebTemplateQualityFixture.Clear"><summary>Clears text.</summary><returns>No value.</returns></member>
            </members></doc>
            """);
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions
        {
            XmlPath = xml, AssemblyPath = typeof(WebTemplateQualityFixture).Assembly.Location,
            OutputPath = output, Template = "docs", Format = "html", IncludeUndocumentedTypes = false,
            BodyClass = "custom-body", ApiSuiteCurrentId = "Quality Suite", SourceRootPath = FindRepositoryRoot(),
            SourceUrlPattern = sourcePattern
        });
        return File.ReadAllText(Path.Combine(output, "powerforge-tests-webtemplatequalityfixture", "index.html"));
    }

    private static string FindRepositoryRoot()
    {
        // The portable PDB records the actual source directory, independent of artifacts output placement.
        return Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.FullName;
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private const string HelpXml = """
        <helpItems xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
          <command:command>
            <command:details><command:name>Get-Demo</command:name><maml:description><maml:para>Gets a demo.</maml:para></maml:description></command:details>
            <command:syntax><command:syntaxItem><maml:name>Get-Demo</maml:name><command:parameter required="true" position="0" pipelineInput="True (ByValue)"><maml:name>Name</maml:name><command:parameterValue required="true">String</command:parameterValue></command:parameter></command:syntaxItem></command:syntax>
            <command:examples><command:example><maml:title>---------- EXAMPLE 1 ----------</maml:title><dev:code>Get-Demo -Name Example</dev:code></command:example></command:examples>
          </command:command>
        </helpItems>
        """;
}

public class WebTemplateQualityFixture
{
    public string Build(string name = "Example") => name;
    public void Clear() { }
}
