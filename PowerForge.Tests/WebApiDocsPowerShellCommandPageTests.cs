using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebApiDocsPowerShellCommandPageTests
{
    private const string HelpXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
          <command:command>
            <command:details>
              <command:name>New-DemoChart</command:name>
              <maml:description><maml:para>Creates a demo chart.</maml:para></maml:description>
            </command:details>
            <maml:description>
              <maml:para>First description paragraph.</maml:para>
              <maml:para>Second description paragraph.</maml:para>
            </maml:description>
            <command:syntax>
              <command:syntaxItem>
                <maml:name>New-DemoChart</maml:name>
                <command:parameter required="true" pipelineInput="False" position="0" aliases="None">
                  <maml:name>Path</maml:name>
                  <maml:description><maml:para>Output path.</maml:para></maml:description>
                  <command:parameterValue required="true">String</command:parameterValue>
                </command:parameter>
                <command:parameter required="true" pipelineInput="False" position="named">
                  <maml:name>Definition</maml:name>
                  <maml:description><maml:para>Chart definitions.</maml:para></maml:description>
                  <command:parameterValue required="true">ScriptBlock</command:parameterValue>
                </command:parameter>
                <command:parameter required="false" pipelineInput="True (ByValue)" position="named">
                  <maml:name>Theme</maml:name>
                  <maml:description><maml:para>Chart theme.</maml:para></maml:description>
                  <command:parameterValue required="false">ChartTheme</command:parameterValue>
                  <command:parameterValueGroup>
                    <command:parameterValue>Default</command:parameterValue>
                    <command:parameterValue>Dark</command:parameterValue>
                    <command:parameterValue>Light</command:parameterValue>
                    <command:parameterValue>Colorblind</command:parameterValue>
                    <command:parameterValue>Aurora</command:parameterValue>
                    <command:parameterValue>Editorial</command:parameterValue>
                  </command:parameterValueGroup>
                </command:parameter>
              </command:syntaxItem>
              <command:syntaxItem>
                <maml:name>New-DemoChart</maml:name>
                <command:parameter required="false" pipelineInput="False" position="0">
                  <maml:name>Path</maml:name>
                  <maml:description><maml:para>Output path.</maml:para></maml:description>
                  <command:parameterValue required="false">String</command:parameterValue>
                </command:parameter>
                <command:parameter required="true" pipelineInput="False" position="named">
                  <maml:name>Chart</maml:name>
                  <maml:description><maml:para>An existing chart.</maml:para></maml:description>
                  <command:parameterValue required="true">Chart</command:parameterValue>
                </command:parameter>
              </command:syntaxItem>
            </command:syntax>
            <command:returnValues>
              <command:returnValue>
                <dev:type><maml:name>Demo.Chart</maml:name></dev:type>
              </command:returnValue>
            </command:returnValues>
            <command:examples>
              <command:example>
                <maml:title>Script block with XML indentation</maml:title>
                <maml:introduction>
                  <maml:para>PS&gt; </maml:para>
                </maml:introduction>
                <dev:code>New-DemoChart -Path chart.png -Definition {
                        New-DemoBar -Value 1,2,3
                    } -Theme Dark</dev:code>
                <dev:remarks><maml:para>Renders a bar chart.</maml:para></dev:remarks>
              </command:example>
              <command:example>
                <maml:title>Pipeline continuation keeps its indentation</maml:title>
                <maml:introduction>
                  <maml:para>PS C:\&gt; </maml:para>
                </maml:introduction>
                <dev:code>Get-DemoData |
            Where-Object Enabled</dev:code>
              </command:example>
              <command:example>
                <maml:title>Prompt inside the code</maml:title>
                <dev:code>PS C:\&gt; New-DemoChart -Path out.png -Chart $chart</dev:code>
              </command:example>
            </command:examples>
          </command:command>
        </helpItems>
        """;

    [Fact]
    public void Generate_PowerShellCommandPage_UsesCommandReferenceChrome()
    {
        var root = CreateRoot("chrome");
        try
        {
            var (outputPath, _) = GeneratePowerShell(root, options => options.PowerShellModuleName = "DemoModule");
            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "new-demochart", "index.html"));
            var indexHtml = File.ReadAllText(Path.Combine(outputPath, "index.html"));

            Assert.Contains("<a href=\"/powershell/commands/\">Commands</a>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("API Reference", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"ev-eyebrow\"", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span class=\"type-meta-label\">Module</span>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<code>DemoModule</code>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Namespace<", typeHtml, StringComparison.Ordinal);

            // One module and one command kind: the sidebar has nothing to filter.
            Assert.DoesNotContain("class=\"sidebar-filters\"", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"api-namespace\"", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span>DemoModule</span>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("data-unit=\"commands\"", typeHtml, StringComparison.Ordinal);

            Assert.Contains("<p class=\"ev-eyebrow\">Commands</p>", indexHtml, StringComparison.Ordinal);
            Assert.Contains("<h2>All Commands</h2>", indexHtml, StringComparison.Ordinal);
            Assert.Contains("in DemoModule.</p>", indexHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("All Namespaces", indexHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellCommandPage_HonorsSectionLabelAndTemplateToken()
    {
        var root = CreateRoot("label");
        try
        {
            var templatePath = Path.Combine(root, "docs-type.html");
            File.WriteAllText(templatePath, "<html><body><button>{{SECTION_LABEL}}</button>{{SIDEBAR}}{{MAIN}}</body></html>");
            var (outputPath, _) = GeneratePowerShell(root, options =>
            {
                options.SectionLabel = "Cmdlet reference";
                options.DocsTypeTemplatePath = templatePath;
            });
            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "new-demochart", "index.html"));

            Assert.Contains("<button>Cmdlet reference</button>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<a href=\"/powershell/commands/\">Cmdlet reference</a>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span>Cmdlet reference</span>", typeHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellModuleName_TakesCasingFromMatchingPackageId()
    {
        var root = CreateRoot("casing");
        try
        {
            var (outputPath, _) = GeneratePowerShell(root, options => options.PackageId = "DemoModule", helpFileName: "demomodule-help.xml");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "new-demochart.json")));
            Assert.Equal("DemoModule", json.RootElement.GetProperty("namespace").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellExamples_DropPromptOnlyIntroductionsAndDedentCode()
    {
        var root = CreateRoot("examples");
        try
        {
            var (outputPath, _) = GeneratePowerShell(root, null);
            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "new-demochart", "index.html"));

            Assert.DoesNotContain("<p>PS&gt;</p>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<p>PS C:\\&gt;</p>", typeHtml, StringComparison.Ordinal);
            Assert.Contains(
                "<pre class=\"language-powershell example-code-block\"><code class=\"language-powershell\">New-DemoChart -Path chart.png -Definition {\n    New-DemoBar -Value 1,2,3\n} -Theme Dark</code></pre>",
                typeHtml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<code class=\"language-powershell\">Get-DemoData |\n    Where-Object Enabled</code>",
                typeHtml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<pre class=\"language-powershell example-code-block example-code-block--has-prompt\"><span class=\"example-code__prompt\" aria-hidden=\"true\">PS C:\\&gt; </span><code class=\"language-powershell example-code__body\">New-DemoChart -Path out.png -Chart $chart</code></pre>",
                typeHtml,
                StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellCommandPage_ListsSyntaxThenParametersOnce()
    {
        var root = CreateRoot("parameters");
        try
        {
            var (outputPath, _) = GeneratePowerShell(root, null);
            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "new-demochart", "index.html"));

            Assert.Contains("New-DemoChart [-Path] &lt;String&gt; -Definition &lt;ScriptBlock&gt; [-Theme &lt;ChartTheme&gt;] [&lt;CommonParameters&gt;]", typeHtml, StringComparison.Ordinal);
            Assert.Contains("New-DemoChart [[-Path] &lt;String&gt;] -Chart &lt;Chart&gt; [&lt;CommonParameters&gt;]", typeHtml, StringComparison.Ordinal);

            var syntax = typeHtml.IndexOf("id=\"syntax\"", StringComparison.Ordinal);
            var description = typeHtml.IndexOf("<h2>Description</h2>", StringComparison.Ordinal);
            var examples = typeHtml.IndexOf("id=\"examples\"", StringComparison.Ordinal);
            var parameters = typeHtml.IndexOf("id=\"parameters\"", StringComparison.Ordinal);
            var common = typeHtml.IndexOf("id=\"common-parameters\"", StringComparison.Ordinal);
            Assert.True(syntax > 0 && syntax < description && description < examples && examples < parameters && parameters < common,
                "Expected Get-Help order: syntax, description, examples, parameters, common parameters.");
            Assert.Contains("<p>First description paragraph.</p>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<p>Second description paragraph.</p>", typeHtml, StringComparison.Ordinal);

            Assert.Equal(1, CountOccurrences(typeHtml, "<dt id=\"parameter-path\">"));
            Assert.Equal(1, CountOccurrences(typeHtml, "<dd>Output path.</dd>"));
            Assert.Contains("required in By Definition + Path", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span class=\"param-meta-chip\">position: 0</span>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span class=\"param-meta-chip pipeline\">pipeline: True (ByValue)</span>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span class=\"param-meta-chip sets\">sets: By Chart</span>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("position: named", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("pipeline: False", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"member-toolbar\"", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<h3>Outputs</h3>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"#parameters\">Parameters</a>", typeHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellCommandPage_SkipsDescriptionAndOutputsThatRepeatTheMeta()
    {
        var root = CreateRoot("repeats");
        try
        {
            var helpPath = Path.Combine(root, "Demo.Module-help.xml");
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-DemoInfo</command:name>
                      <maml:description><maml:para>Reads demo information.</maml:para></maml:description>
                    </command:details>
                    <maml:description><maml:para>Reads demo information.</maml:para></maml:description>
                    <command:syntax>
                      <command:syntaxItem>
                        <maml:name>Get-DemoInfo</maml:name>
                        <command:parameter required="true" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">String</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:returnValues>
                      <command:returnValue><dev:type><maml:name>Demo.First</maml:name></dev:type></command:returnValue>
                      <command:returnValue><dev:type><maml:name>Demo.Second</maml:name></dev:type></command:returnValue>
                    </command:returnValues>
                  </command:command>
                </helpItems>
                """);
            var outputPath = Path.Combine(root, "_site", "commands");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                OutputPath = outputPath,
                BaseUrl = "/commands",
                Template = "docs",
                Format = "html",
                GeneratePowerShellFallbackExamples = false
            });

            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "get-demoinfo", "index.html"));
            Assert.Contains("<p class=\"type-summary\">Reads demo information.</p>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<h2>Description</h2>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<code>Demo.Second</code>", typeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"outputs\"", typeHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_CSharpDocs_KeepApiReferenceChrome()
    {
        var root = CreateRoot("csharp");
        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Test</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Sample">
                      <summary>Sample.</summary>
                      <example><code>var sample = new Sample();</code></example>
                    </member>
                    <member name="M:MyNamespace.Sample.Run(System.String)">
                      <summary>Runs.</summary>
                      <param name="name">Name.</param>
                    </member>
                  </members>
                </doc>
                """);
            var outputPath = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Format = "html",
                Template = "docs",
                BaseUrl = "/api"
            });

            var typeHtml = File.ReadAllText(Path.Combine(outputPath, "mynamespace-sample", "index.html"));
            var indexHtml = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            Assert.Contains("<a href=\"/api/\">API Reference</a>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<p class=\"ev-eyebrow\">API Reference</p>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<span class=\"type-meta-label\">Namespace</span>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<label for=\"api-namespace\" class=\"filter-label\">Namespace</label>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("class=\"member-toolbar\"", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<h3>Parameters</h3>", typeHtml, StringComparison.Ordinal);
            // Example code renders without the blank first line and indented closing tag inside <pre>.
            Assert.Contains("<pre class=\"language-csharp example-code-block\"><code class=\"language-csharp\">var sample = new Sample();</code></pre>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<h2>All Namespaces</h2>", indexHtml, StringComparison.Ordinal);
            Assert.Contains("<p class=\"ev-eyebrow\">API Reference</p>", indexHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_MapsSectionLabelAndModuleName()
    {
        var root = CreateRoot("pipeline");
        try
        {
            File.WriteAllText(Path.Combine(root, "demomodule-help.xml"), HelpXml);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./demomodule-help.xml",
                      "out": "./_site/commands",
                      "baseUrl": "/commands",
                      "format": "both",
                      "template": "docs",
                      "sectionLabel": "Cmdlets",
                      "moduleName": "DemoModule",
                      "generatePowerShellFallbackExamples": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Steps.Select(static step => step.Message)));
            var typeHtml = File.ReadAllText(Path.Combine(root, "_site", "commands", "new-demochart", "index.html"));
            Assert.Contains("<a href=\"/commands/\">Cmdlets</a>", typeHtml, StringComparison.Ordinal);
            Assert.Contains("<code>DemoModule</code>", typeHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static (string OutputPath, WebApiDocsResult Result) GeneratePowerShell(
        string root,
        Action<WebApiDocsOptions>? configure,
        string helpFileName = "Demo.Module-help.xml")
    {
        var helpPath = Path.Combine(root, helpFileName);
        File.WriteAllText(helpPath, HelpXml);
        var outputPath = Path.Combine(root, "_site", "powershell", "commands");
        var options = new WebApiDocsOptions
        {
            Type = ApiDocsType.PowerShell,
            HelpPath = helpPath,
            OutputPath = outputPath,
            Title = "Demo commands",
            BaseUrl = "/powershell/commands",
            Template = "docs",
            Format = "both",
            GeneratePowerShellFallbackExamples = false
        };
        configure?.Invoke(options);
        var result = WebApiDocsGenerator.Generate(options);
        return (outputPath, result);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string CreateRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-pscommand-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
            // best effort cleanup in tests
        }
    }
}
