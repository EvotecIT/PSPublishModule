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

    [Fact]
    public void Generate_PowerShellHelp_ClassifiesCmdletFunctionAndAliasFromManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-kinds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <maml:description><maml:para>Gets data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                  <command:command>
                    <command:details>
                      <command:name>Invoke-SampleFunction</command:name>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Invoke-SampleFunction</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                  <command:command>
                    <command:details>
                      <command:name>ss</command:name>
                      <maml:description><maml:para>Alias command.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>ss</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport   = @('Get-SampleCmdlet')
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport   = @('ss')
                    RootModule        = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleFunction {
                    [CmdletBinding()]
                    param()
                }
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(3, result.TypeCount);

            using var cmdletJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "get-samplecmdlet.json")));
            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            using var aliasJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "ss.json")));

            Assert.Equal("Cmdlet", cmdletJson.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Function", functionJson.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Alias", aliasJson.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_ImportsAboutTopicsAndLinksThemFromRemarks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-about-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <maml:description><maml:para>Gets data.</maml:para></maml:description>
                    </command:details>
                    <maml:description>
                      <maml:para>For more details, see about_SampleTopic.</maml:para>
                    </maml:description>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "about_SampleTopic.help.txt"),
                """
                # about_SampleTopic

                Describes sample topic behavior.

                Use this topic to learn extra details.
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "html"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(2, result.TypeCount);

            using var aboutJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "about-sampletopic.json")));
            Assert.Equal("About", aboutJson.RootElement.GetProperty("kind").GetString());
            Assert.Equal("about_SampleTopic", aboutJson.RootElement.GetProperty("name").GetString());

            var commandHtml = File.ReadAllText(Path.Combine(outputPath, "get-samplecmdlet", "index.html"));
            Assert.Contains("href=\"/api/powershell/about-sampletopic/\"", commandHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_ParsesManifestMultilineExportsAndComments()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-manifest-parse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <maml:description><maml:para>Gets data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                  <command:command>
                    <command:details>
                      <command:name>Invoke-SampleFunction</command:name>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Invoke-SampleFunction</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                  <command:command>
                    <command:details>
                      <command:name>ss</command:name>
                      <maml:description><maml:para>Alias command.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>ss</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @(
                        'Get-SampleCmdlet' # primary cmdlet export
                    )
                    FunctionsToExport = @(
                        "Invoke-SampleFunction"
                    )
                    AliasesToExport = @(
                        'ss'
                    )
                    RootModule = "Sample.Module.psm1" # local script module
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleFunction {
                    [CmdletBinding()]
                    param()
                }
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(3, result.TypeCount);

            using var cmdletJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "get-samplecmdlet.json")));
            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            using var aliasJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "ss.json")));

            Assert.Equal("Cmdlet", cmdletJson.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Function", functionJson.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Alias", aliasJson.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_PrefersCommandTypeFromHelpXmlOverManifestHints()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-command-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Invoke-SampleFunction</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Invoke-SampleFunction</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = '*'
                    FunctionsToExport = @()
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param() }");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);
            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            Assert.Equal("Function", functionJson.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_ImportsFallbackExamplesFromScriptsWhenHelpHasNoExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-fallback-examples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Invoke-SampleFunction</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Invoke-SampleFunction</command:name>
                        <command:parameter required="true">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="true">
                        <maml:name>Name</maml:name>
                        <maml:description><maml:para>Name value.</maml:para></maml:description>
                        <command:parameterValue required="true">string</command:parameterValue>
                      </command:parameter>
                    </command:parameters>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Example.Basic.ps1"),
                """
                # Demo script
                Invoke-SampleFunction -Name "FromScript"
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);
            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            var examples = functionJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            Assert.Contains(examples,
                ex => ex.GetProperty("kind").GetString() == "code" &&
                      ex.GetProperty("text").GetString()!.Contains("Invoke-SampleFunction -Name \"FromScript\"", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_ApiDocs_WritesCoverageReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <maml:description><maml:para>Gets data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
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
                Format = "json",
                CoverageReportPath = "reports/api-coverage.json"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.False(string.IsNullOrWhiteSpace(result.CoveragePath));
            Assert.True(File.Exists(result.CoveragePath!));

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            Assert.Equal(1, coverage.RootElement.GetProperty("types").GetProperty("count").GetInt32());
            Assert.Equal(1, coverage.RootElement.GetProperty("powershell").GetProperty("commandCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
