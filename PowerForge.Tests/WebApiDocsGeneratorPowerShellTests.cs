using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebApiDocsGeneratorPowerShellTests
{
    [Fact]
    public void Generate_PowerShellHelp_RendersExamplesAndNormalizesModuleNamespace()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module.dll-Help.xml");
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>New-SampleCmdlet</command:name>
                      <maml:description>
                        <maml:para>Creates a sample item.</maml:para>
                      </maml:description>
                    </command:details>
                    <maml:description>
                      <maml:para>Sample cmdlet remarks.</maml:para>
                    </maml:description>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>New-SampleCmdlet</command:name>
                        <command:parameter required="true" globbing="false" pipelineInput="false" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                        </command:parameter>
                        <command:parameter required="false" defaultValue="General" globbing="false" pipelineInput="false" position="named">
                          <maml:name>Category</maml:name>
                          <command:parameterValue required="false">string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="true" globbing="false" pipelineInput="false" position="named">
                        <maml:name>Name</maml:name>
                        <maml:description>
                          <maml:para>Name value from command parameters.</maml:para>
                        </maml:description>
                        <command:parameterValue required="true">string</command:parameterValue>
                      </command:parameter>
                      <command:parameter required="false" defaultValue="General" globbing="false" pipelineInput="false" position="named">
                        <maml:name>Category</maml:name>
                        <maml:description>
                          <maml:para>Category from command parameters.</maml:para>
                        </maml:description>
                        <command:parameterValue required="false">string</command:parameterValue>
                      </command:parameter>
                    </command:parameters>
                    <command:examples>
                      <command:example>
                        <maml:title>----------  Example 1: Basic usage.  ----------</maml:title>
                        <dev:code>New-SampleCmdlet -Name "Demo"</dev:code>
                        <dev:remarks>
                          <maml:para>Use this in scripts.</maml:para>
                        </dev:remarks>
                      </command:example>
                    </command:examples>
                  </command:command>
                </helpItems>
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "html"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(1, result.TypeCount);

            var typeJsonPath = Path.Combine(outputPath, "types", "new-samplecmdlet.json");
            Assert.True(File.Exists(typeJsonPath));

            using var json = JsonDocument.Parse(File.ReadAllText(typeJsonPath));
            var rootElement = json.RootElement;
            Assert.Equal("Sample.Module", rootElement.GetProperty("namespace").GetString());

            var examples = rootElement.GetProperty("examples");
            Assert.True(examples.GetArrayLength() >= 2);
            Assert.Contains(examples.EnumerateArray(),
                ex => ex.GetProperty("kind").GetString() == "text" &&
                      ex.GetProperty("text").GetString()!.Contains("Example 1: Basic usage.", StringComparison.Ordinal));
            Assert.Contains(examples.EnumerateArray(),
                ex => ex.GetProperty("kind").GetString() == "code" &&
                      ex.GetProperty("text").GetString()!.Contains("New-SampleCmdlet -Name \"Demo\"", StringComparison.Ordinal));

            var methods = rootElement.GetProperty("methods");
            var parameters = methods[0].GetProperty("parameters");
            Assert.False(parameters[0].GetProperty("isOptional").GetBoolean());
            Assert.Equal("Name value from command parameters.", parameters[0].GetProperty("summary").GetString());
            Assert.True(parameters[1].GetProperty("isOptional").GetBoolean());
            Assert.Equal("General", parameters[1].GetProperty("defaultValue").GetString());
            Assert.Equal("Category from command parameters.", parameters[1].GetProperty("summary").GetString());

            var htmlPath = Path.Combine(outputPath, "new-samplecmdlet", "index.html");
            Assert.True(File.Exists(htmlPath));
            var html = File.ReadAllText(htmlPath);
            Assert.Contains("type-examples", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("New-SampleCmdlet -Name", html, StringComparison.Ordinal);
            Assert.Contains("Name value from command parameters.", html, StringComparison.Ordinal);
            Assert.Contains("Category from command parameters.", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
