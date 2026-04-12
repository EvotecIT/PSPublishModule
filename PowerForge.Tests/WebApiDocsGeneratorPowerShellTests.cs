using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
                ex =>
                {
                    var kind = ex.GetProperty("kind").GetString();
                    return (string.Equals(kind, "heading", StringComparison.Ordinal) ||
                           string.Equals(kind, "text", StringComparison.Ordinal)) &&
                           ex.GetProperty("text").GetString()!.Contains("Example 1: Basic usage.", StringComparison.Ordinal);
                });
            Assert.Contains(examples.EnumerateArray(),
                ex => ex.GetProperty("kind").GetString() == "heading");
            Assert.Contains(examples.EnumerateArray(),
                ex => ex.GetProperty("kind").GetString() == "code" &&
                      ex.GetProperty("origin").GetString() == "AuthoredHelp" &&
                      ex.GetProperty("text").GetString()!.Contains("New-SampleCmdlet -Name \"Demo\"", StringComparison.Ordinal));
            Assert.True(rootElement.TryGetProperty("inputTypes", out _));
            Assert.True(rootElement.TryGetProperty("outputTypes", out _));

            var methods = rootElement.GetProperty("methods");
            Assert.True(methods[0].GetProperty("includesCommonParameters").GetBoolean());
            var parameters = methods[0].GetProperty("parameters");
            Assert.False(parameters[0].GetProperty("isOptional").GetBoolean());
            Assert.Equal("Name value from command parameters.", parameters[0].GetProperty("summary").GetString());
            Assert.Equal("named", parameters[0].GetProperty("position").GetString());
            Assert.Equal("false", parameters[0].GetProperty("pipelineInput").GetString());
            Assert.True(parameters[1].GetProperty("isOptional").GetBoolean());
            Assert.Equal("General", parameters[1].GetProperty("defaultValue").GetString());
            Assert.Equal("Category from command parameters.", parameters[1].GetProperty("summary").GetString());
            Assert.Equal("named", parameters[1].GetProperty("position").GetString());
            Assert.Equal("false", parameters[1].GetProperty("pipelineInput").GetString());

            var htmlPath = Path.Combine(outputPath, "new-samplecmdlet", "index.html");
            Assert.True(File.Exists(htmlPath));
            var html = File.ReadAllText(htmlPath);
            Assert.Contains("type-examples", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("New-SampleCmdlet -Name", html, StringComparison.Ordinal);
            Assert.Contains("<pre class=\"member-signature language-powershell\"><code class=\"language-powershell\">", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<pre class=\"language-powershell\"><code class=\"language-powershell\">", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("New-SampleCmdlet -Name &quot;Demo&quot;" + Environment.NewLine + "        </code></pre>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("New-SampleCmdlet -Name &quot;Demo&quot;        </code></pre>", html, StringComparison.Ordinal);
            Assert.Contains("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
            Assert.Contains("prism-core.min.js", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prism-autoloader.min.js", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("plugins.autoloader.languages_path", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("setTimeout(run,delayMs)", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Authored help example", html, StringComparison.Ordinal);
            Assert.Contains("example-origin-badge example-origin-authored", html, StringComparison.Ordinal);

            var bootstrapIndex = html.IndexOf("window.Prism=window.Prism||{};window.Prism.manual=true;", StringComparison.Ordinal);
            var coreIndex = html.IndexOf("prism-core.min.js", StringComparison.OrdinalIgnoreCase);
            var autoloaderIndex = html.IndexOf("prism-autoloader.min.js", StringComparison.OrdinalIgnoreCase);
            Assert.True(bootstrapIndex >= 0, "Expected Prism bootstrap script.");
            Assert.True(coreIndex > bootstrapIndex, "Prism core should load after Prism manual bootstrap.");
            Assert.True(autoloaderIndex > coreIndex, "Prism autoloader should load after Prism core.");
            Assert.Contains("[&lt;CommonParameters&gt;]", html, StringComparison.Ordinal);
            Assert.Contains("Common Parameters", html, StringComparison.Ordinal);
            Assert.Contains("Name value from command parameters.", html, StringComparison.Ordinal);
            Assert.Contains("Category from command parameters.", html, StringComparison.Ordinal);
            Assert.Contains("position: named", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pipeline: false", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_DoesNotInjectPrismAssets_WhenPrismModeOff()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-prism-off-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Get-SampleData</command:name>
                      <maml:description><maml:para>Gets sample data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Get-SampleData</command:name>
                        <command:parameter required="true" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
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
                Format = "html",
                Prism = new PrismSpec { Mode = "off" }
            };

            var result = WebApiDocsGenerator.Generate(options);
            var html = File.ReadAllText(Path.Combine(outputPath, "get-sampledata", "index.html"));

            Assert.Contains("language-powershell", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prism-core", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prism-autoloader", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("plugins.autoloader.languages_path", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
    public void Generate_PowerShellHelp_ImportsAboutTopicsFromCaseInsensitiveAndScriptCultureFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-about-cultures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-us", "Sample.Module-help.xml");
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
                      <maml:para>See about_LowerCaseTopic and about_ScriptTopic for more details.</maml:para>
                    </maml:description>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            var lowerCaseCulturePath = Path.Combine(root, "en-us");
            var scriptCulturePath = Path.Combine(root, "zh-Hans");
            Directory.CreateDirectory(scriptCulturePath);
            File.WriteAllText(Path.Combine(lowerCaseCulturePath, "about_LowerCaseTopic.help.txt"),
                """
                # about_LowerCaseTopic

                Loaded from a lowercase culture folder.
                """);
            File.WriteAllText(Path.Combine(scriptCulturePath, "about_ScriptTopic.help.txt"),
                """
                # about_ScriptTopic

                Loaded from a script culture folder.
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
            Assert.Equal(3, result.TypeCount);

            using var lowerCaseJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "about-lowercasetopic.json")));
            using var scriptJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "about-scripttopic.json")));
            Assert.Equal("about_LowerCaseTopic", lowerCaseJson.RootElement.GetProperty("name").GetString());
            Assert.Equal("about_ScriptTopic", scriptJson.RootElement.GetProperty("name").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_ImportsRootAboutTopicsForNeutralCultureFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-about-neutral-culture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpDirectory = Path.Combine(root, "en");
            Directory.CreateDirectory(helpDirectory);
            var helpPath = Path.Combine(helpDirectory, "Sample.Module-help.xml");
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
                      <maml:para>For more details, see about_RootTopic.</maml:para>
                    </maml:description>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "about_RootTopic.help.txt"),
                """
                # about_RootTopic

                Loaded from the module root while help XML lives in a neutral culture folder.
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
            Assert.Equal(2, result.TypeCount);

            using var aboutJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "about-roottopic.json")));
            Assert.Equal("about_RootTopic", aboutJson.RootElement.GetProperty("name").GetString());
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
    public void Generate_PowerShellHelp_EmitsGitFreshnessMetadataForCommandsAndAboutTopics()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-freshness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpDirectory = Path.Combine(root, "en-US");
            Directory.CreateDirectory(helpDirectory);
            var helpPath = Path.Combine(helpDirectory, "Sample.Module-help.xml");
            var aboutPath = Path.Combine(root, "about_SampleTopic.help.txt");

            RunGit(root, "init");
            RunGit(root, "config", "user.email", "tests@example.invalid");
            RunGit(root, "config", "user.name", "PowerForge Tests");

            File.WriteAllText(aboutPath,
                """
                # about_SampleTopic

                Describes sample topic behavior.
                """);
            RunGit(root, "add", "about_SampleTopic.help.txt");
            var olderCommitDate = DateTimeOffset.UtcNow.AddDays(-40).ToString("yyyy-MM-ddTHH:mm:ssK");
            RunGit(root,
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = olderCommitDate,
                    ["GIT_COMMITTER_DATE"] = olderCommitDate
                },
                "commit", "-m", "Add about topic");

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
            RunGit(root, "add", "en-US/Sample.Module-help.xml");
            var recentCommitDate = DateTimeOffset.UtcNow.AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ssK");
            RunGit(root,
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = recentCommitDate,
                    ["GIT_COMMITTER_DATE"] = recentCommitDate
                },
                "commit", "-m", "Add help xml");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both",
                GenerateGitFreshness = true,
                GitFreshnessNewDays = 14,
                GitFreshnessUpdatedDays = 90
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(2, result.TypeCount);

            using var commandJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "get-samplecmdlet.json")));
            using var aboutJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "about-sampletopic.json")));

            var commandFreshness = commandJson.RootElement.GetProperty("freshness");
            var aboutFreshness = aboutJson.RootElement.GetProperty("freshness");

            Assert.Equal("new", commandFreshness.GetProperty("status").GetString());
            Assert.True(commandFreshness.GetProperty("ageDays").GetInt32() <= 14);
            Assert.EndsWith("en-US/Sample.Module-help.xml", commandFreshness.GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, commandFreshness.GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);

            Assert.Equal("updated", aboutFreshness.GetProperty("status").GetString());
            Assert.True(aboutFreshness.GetProperty("ageDays").GetInt32() >= 30);
            Assert.EndsWith("about_SampleTopic.help.txt", aboutFreshness.GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, aboutFreshness.GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);

            var commandHtml = File.ReadAllText(Path.Combine(outputPath, "get-samplecmdlet", "index.html"));
            var aboutHtml = File.ReadAllText(Path.Combine(outputPath, "about-sampletopic", "index.html"));
            Assert.Contains("freshness-badge new", commandHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">new<", commandHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("freshness-badge updated", aboutHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">updated<", aboutHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_UsesDetachedCommandMetadataForFunctionsAndAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-detached-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var artifactRoot = Path.Combine(root, "WebsiteArtifacts", "apidocs", "powershell");
            Directory.CreateDirectory(artifactRoot);
            var helpPath = Path.Combine(artifactRoot, "Sample.Module-help.xml");
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Add-HTML</command:name>
                      <maml:description><maml:para>Adds HTML output.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Add-HTML</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);
            var metadataPath = Path.Combine(artifactRoot, "command-metadata.json");
            File.WriteAllText(metadataPath,
                """
                {
                  "commands": [
                    {
                      "name": "Add-HTML",
                      "kind": "Function",
                      "aliases": [ "EmailHTML" ]
                    }
                  ]
                }
                """);

            var moduleRoot = Path.Combine(root, "module");
            Directory.CreateDirectory(moduleRoot);
            var manifestPath = Path.Combine(moduleRoot, "Sample.Module.psd1");
            File.WriteAllText(manifestPath,
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Add-HTML')
                    AliasesToExport = @('EmailHTML')
                }
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = artifactRoot,
                PowerShellModuleManifestPath = manifestPath,
                PowerShellCommandMetadataPath = metadataPath,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(1, result.TypeCount);

            using var typeJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "add-html.json")));
            Assert.Equal("Function", typeJson.RootElement.GetProperty("kind").GetString());
            Assert.Contains(typeJson.RootElement.GetProperty("commandAliases").EnumerateArray(), static item => item.GetString() == "EmailHTML");

            var indexHtml = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            Assert.Contains("Functions (1)", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EmailHTML", indexHtml, StringComparison.Ordinal);

            var html = File.ReadAllText(Path.Combine(outputPath, "add-html", "index.html"));
            Assert.Contains("Aliases", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EmailHTML", html, StringComparison.Ordinal);
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

            var result = WebApiDocsGenerator.Generate(options);
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
    public void Generate_PowerShellHelp_InfersParameterSetLabelsAndRendersCommonParametersSection()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-parameter-sets-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Get-SampleData</command:name>
                      <maml:description><maml:para>Gets sample data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem parameterSetName="ByName">
                        <command:name>Get-SampleData</command:name>
                        <command:parameter required="true" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                      <command:syntaxItem>
                        <command:name>Get-SampleData</command:name>
                        <command:parameter required="true" position="named">
                          <maml:name>Id</maml:name>
                          <command:parameterValue required="true">int</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="true" position="named">
                        <maml:name>Name</maml:name>
                        <maml:description><maml:para>Name selector.</maml:para></maml:description>
                        <command:parameterValue required="true">string</command:parameterValue>
                      </command:parameter>
                      <command:parameter required="true" position="named">
                        <maml:name>Id</maml:name>
                        <maml:description><maml:para>Numeric selector.</maml:para></maml:description>
                        <command:parameterValue required="true">int</command:parameterValue>
                      </command:parameter>
                    </command:parameters>
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
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "get-sampledata.json")));
            var methods = json.RootElement.GetProperty("methods");
            Assert.Equal(2, methods.GetArrayLength());
            Assert.Equal("ByName", methods[0].GetProperty("parameterSetName").GetString());
            Assert.Equal("By Id", methods[1].GetProperty("parameterSetName").GetString());
            Assert.True(methods[0].GetProperty("includesCommonParameters").GetBoolean());
            Assert.True(methods[1].GetProperty("includesCommonParameters").GetBoolean());
            Assert.Contains("[<CommonParameters>]", methods[0].GetProperty("signature").GetString(), StringComparison.Ordinal);
            Assert.Contains("[<CommonParameters>]", methods[1].GetProperty("signature").GetString(), StringComparison.Ordinal);

            var html = File.ReadAllText(Path.Combine(outputPath, "get-sampledata", "index.html"));
            Assert.Contains("id=\"common-parameters\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("about_CommonParameters", html, StringComparison.Ordinal);
            Assert.Contains("Parameter set: <code>ByName</code>", html, StringComparison.Ordinal);
            Assert.Contains("Parameter set: <code>By Id</code>", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_RendersValidateSetAndEnumValuesInSyntaxAndParameters()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-possible-values-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Invoke-SampleMode</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes sample mode.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Invoke-SampleMode</command:name>
                        <command:parameter required="true" position="named">
                          <maml:name>Mode</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                          <command:parameterValueGroup>
                            <command:parameterValue required="false" variableLength="false">Basic</command:parameterValue>
                            <command:parameterValue required="false" variableLength="false">Advanced</command:parameterValue>
                          </command:parameterValueGroup>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="true" position="named">
                        <maml:name>Mode</maml:name>
                        <maml:description><maml:para>Mode selector.</maml:para></maml:description>
                        <command:parameterValue required="true">string</command:parameterValue>
                        <command:parameterValueGroup>
                          <command:parameterValue required="false" variableLength="false">Basic</command:parameterValue>
                          <command:parameterValue required="false" variableLength="false">Advanced</command:parameterValue>
                        </command:parameterValueGroup>
                      </command:parameter>
                    </command:parameters>
                  </command:command>
                </helpItems>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleMode')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleMode { param([string]$Mode) }");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplemode.json")));
            var method = json.RootElement.GetProperty("methods")[0];
            var parameter = method.GetProperty("parameters")[0];

            Assert.Contains("Basic|Advanced", method.GetProperty("signature").GetString(), StringComparison.Ordinal);
            Assert.Equal(2, parameter.GetProperty("possibleValues").GetArrayLength());
            Assert.Equal("Basic", parameter.GetProperty("possibleValues")[0].GetString());
            Assert.Equal("Advanced", parameter.GetProperty("possibleValues")[1].GetString());

            var html = File.ReadAllText(Path.Combine(outputPath, "invoke-samplemode", "index.html"));
            Assert.Contains("Possible values:", html, StringComparison.Ordinal);
            Assert.Contains("<code>Basic</code>", html, StringComparison.Ordinal);
            Assert.Contains("<code>Advanced</code>", html, StringComparison.Ordinal);

            var examples = json.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            Assert.Contains(examples,
                ex => ex.GetProperty("kind").GetString() == "code" &&
                      ex.GetProperty("text").GetString()!.Contains("Invoke-SampleMode -Mode 'Basic'", StringComparison.Ordinal));
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
                      ex.GetProperty("origin").GetString() == "ImportedScript" &&
                      ex.GetProperty("text").GetString()!.Contains("Invoke-SampleFunction -Name \"FromScript\"", StringComparison.Ordinal));

            var html = File.ReadAllText(Path.Combine(outputPath, "invoke-samplefunction", "index.html"));
            Assert.Contains("Imported script example", html, StringComparison.Ordinal);
            Assert.Contains("example-origin-badge example-origin-imported", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_PrefersCommandSpecificExampleScriptsOverGenericOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-fallback-ranking-" + Guid.NewGuid().ToString("N"));
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
            File.WriteAllText(Path.Combine(examplesDir, "Example.Generic.ps1"),
                """
                Invoke-SampleFunction -Name "FromGeneric"
                """);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.ps1"),
                """
                Invoke-SampleFunction -Name "FromSpecific"
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
                Format = "both",
                PowerShellFallbackExampleLimitPerCommand = 1
            };

            WebApiDocsGenerator.Generate(options);
            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            var examples = functionJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            var codeExamples = examples
                .Where(ex => ex.GetProperty("kind").GetString() == "code")
                .Select(ex => ex.GetProperty("text").GetString())
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            Assert.Single(codeExamples);
            Assert.Contains("FromSpecific", codeExamples[0], StringComparison.Ordinal);
            Assert.DoesNotContain("FromGeneric", codeExamples[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_GeneratesFallbackExamplesFromBestParameterSets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-generated-fallback-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Invoke-SampleFunction</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem parameterSetName="ByName">
                        <command:name>Invoke-SampleFunction</command:name>
                        <command:parameter required="true">
                          <maml:name>Name</maml:name>
                          <command:parameterValue required="true">string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                      <command:syntaxItem parameterSetName="ById">
                        <command:name>Invoke-SampleFunction</command:name>
                        <command:parameter required="true">
                          <maml:name>Id</maml:name>
                          <command:parameterValue required="true">int</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                      <command:syntaxItem parameterSetName="ByInputObject">
                        <command:name>Invoke-SampleFunction</command:name>
                        <command:parameter required="true">
                          <maml:name>InputObject</maml:name>
                          <command:parameterValue required="true">Sample.Module.Item</command:parameterValue>
                        </command:parameter>
                        <command:parameter required="false">
                          <maml:name>Credential</maml:name>
                          <command:parameterValue required="false">pscredential</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="true">
                        <maml:name>Name</maml:name>
                        <maml:description><maml:para>Name value.</maml:para></maml:description>
                        <command:parameterValue required="true">string</command:parameterValue>
                      </command:parameter>
                      <command:parameter required="true">
                        <maml:name>Id</maml:name>
                        <maml:description><maml:para>Identifier.</maml:para></maml:description>
                        <command:parameterValue required="true">int</command:parameterValue>
                      </command:parameter>
                      <command:parameter required="true">
                        <maml:name>InputObject</maml:name>
                        <maml:description><maml:para>Pipeline object.</maml:para></maml:description>
                        <command:parameterValue required="true">Sample.Module.Item</command:parameterValue>
                      </command:parameter>
                      <command:parameter required="false">
                        <maml:name>Credential</maml:name>
                        <maml:description><maml:para>Credential.</maml:para></maml:description>
                        <command:parameterValue required="false">pscredential</command:parameterValue>
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
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name, [int]$Id, $InputObject, [pscredential]$Credential) }");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both",
                PowerShellFallbackExampleLimitPerCommand = 2
            };

            WebApiDocsGenerator.Generate(options);

            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            var examples = functionJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            var codeExamples = examples
                .Where(ex => ex.GetProperty("kind").GetString() == "code")
                .Select(ex => ex.GetProperty("text").GetString())
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            var textExamples = examples
                .Where(ex => ex.GetProperty("kind").GetString() == "text")
                .Select(ex => ex.GetProperty("text").GetString())
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            Assert.Equal(2, codeExamples.Length);
            Assert.Contains(codeExamples, example => example!.Contains("Invoke-SampleFunction -Name 'Name'", StringComparison.Ordinal));
            Assert.Contains(codeExamples, example => example!.Contains("Invoke-SampleFunction -Id 1", StringComparison.Ordinal));
            Assert.DoesNotContain(codeExamples, example => example!.Contains("-InputObject", StringComparison.Ordinal));
            Assert.Contains(textExamples, example => example!.Contains("parameter set 'ByName'", StringComparison.Ordinal));
            Assert.Contains(textExamples, example => example!.Contains("parameter set 'ById'", StringComparison.Ordinal));
            Assert.All(
                examples.Where(ex =>
                {
                    var kind = ex.GetProperty("kind").GetString();
                    return string.Equals(kind, "code", StringComparison.Ordinal) ||
                           string.Equals(kind, "text", StringComparison.Ordinal);
                }),
                ex => Assert.Equal("GeneratedFallback", ex.GetProperty("origin").GetString()));

            var html = File.ReadAllText(Path.Combine(outputPath, "invoke-samplefunction", "index.html"));
            Assert.Contains("Generated fallback example", html, StringComparison.Ordinal);
            Assert.Contains("example-origin-badge example-origin-generated", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_GeneratesCommandOnlyFallbackWhenSyntaxIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-generated-command-only-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Invoke-SampleFunction</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
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
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            using var functionJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-samplefunction.json")));
            var examples = functionJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            Assert.Contains(examples, ex =>
                ex.GetProperty("kind").GetString() == "code" &&
                ex.GetProperty("origin").GetString() == "GeneratedFallback" &&
                ex.GetProperty("text").GetString() == "Invoke-SampleFunction");
        }
        finally
        {
            TryDeleteDirectory(root);
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

    [Fact]
    public void Generate_PowerShellHelp_CoverageDistinguishesAuthoredImportedAndGeneratedExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-example-origins-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Get-AuthoredExample</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Gets authored example content.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Get-AuthoredExample</command:name>
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
                    <command:examples>
                      <command:example>
                        <maml:title>----------  Example 1: Authored.  ----------</maml:title>
                        <dev:code>Get-AuthoredExample -Name "Alpha"</dev:code>
                      </command:example>
                    </command:examples>
                  </command:command>
                  <command:command>
                    <command:details>
                      <command:name>Invoke-ImportedExample</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Gets imported example content.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Invoke-ImportedExample</command:name>
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
                  <command:command>
                    <command:details>
                      <command:name>Set-GeneratedExample</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Gets generated example content.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem parameterSetName="ByName">
                        <command:name>Set-GeneratedExample</command:name>
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
                    FunctionsToExport = @('Get-AuthoredExample', 'Invoke-ImportedExample', 'Set-GeneratedExample')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Get-AuthoredExample { param([string]$Name) }
                function Invoke-ImportedExample { param([string]$Name) }
                function Set-GeneratedExample { param([string]$Name) }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-ImportedExample.ps1"),
                """
                Invoke-ImportedExample -Name "FromScript"
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Format = "json",
                CoverageReportPath = "reports/api-coverage.json"
            };

            var result = WebApiDocsGenerator.Generate(options);
            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var powershell = coverage.RootElement.GetProperty("powershell");

            Assert.Equal(3, powershell.GetProperty("commandCount").GetInt32());
            Assert.Equal(3, powershell.GetProperty("codeExamples").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("authoredHelpCodeExamples").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptCodeExamples").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("generatedFallbackCodeExamples").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("generatedFallbackOnlyExamples").GetProperty("covered").GetInt32());

            Assert.Contains(
                powershell.GetProperty("commandsUsingAuthoredHelpCodeExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Get-AuthoredExample", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptCodeExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-ImportedExample", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingGeneratedFallbackCodeExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Set-GeneratedExample", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingGeneratedFallbackOnlyExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Set-GeneratedExample", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_WarnsWhenCommandsRelyOnlyOnGeneratedFallbackExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-generated-warning-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Invoke-SampleFunction</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem parameterSetName="ByName">
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

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("rely only on generated fallback examples", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleFunction", StringComparison.OrdinalIgnoreCase));

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var generatedFallbackOnly = coverage.RootElement
                .GetProperty("powershell")
                .GetProperty("generatedFallbackOnlyExamples");
            Assert.Equal(1, generatedFallbackOnly.GetProperty("covered").GetInt32());
            var generatedFallback = coverage.RootElement
                .GetProperty("powershell")
                .GetProperty("generatedFallbackCodeExamples");
            Assert.Equal(1, generatedFallback.GetProperty("covered").GetInt32());
            Assert.Equal(0, coverage.RootElement.GetProperty("powershell").GetProperty("authoredHelpCodeExamples").GetProperty("covered").GetInt32());
            Assert.Equal(0, coverage.RootElement.GetProperty("powershell").GetProperty("importedScriptCodeExamples").GetProperty("covered").GetInt32());
            Assert.Contains(
                coverage.RootElement.GetProperty("powershell").GetProperty("commandsUsingGeneratedFallbackOnlyExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleFunction", StringComparison.Ordinal));
            Assert.Contains(
                coverage.RootElement.GetProperty("powershell").GetProperty("commandsUsingGeneratedFallbackCodeExamples").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleFunction", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidatePowerShellExamples_FindsInvalidAndUnmatchedScripts_AndWritesReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) }
                function Invoke-SampleTwo { param([string]$Name) }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "BrokenExample.ps1"), "Invoke-SampleTwo -Name (");
            File.WriteAllText(Path.Combine(examplesDir, "GenericDemo.ps1"), "Get-UnrelatedThing -Name 'Other'");

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                TimeoutSeconds = 60
            });

            Assert.True(validation.ValidationSucceeded);
            Assert.Equal(3, validation.FileCount);
            Assert.Equal(2, validation.ValidSyntaxFileCount);
            Assert.Equal(1, validation.InvalidSyntaxFileCount);
            Assert.Equal(2, validation.MatchedFileCount);
            Assert.Equal(1, validation.UnmatchedFileCount);
            Assert.Equal(2, validation.KnownCommandCount);
            Assert.Contains(validation.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("failed syntax validation", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("BrokenExample.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(validation.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("did not reference any documented commands", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("GenericDemo.ps1", StringComparison.OrdinalIgnoreCase));

            var broken = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "BrokenExample.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.False(broken.ValidSyntax);
            Assert.Contains("Invoke-SampleTwo", broken.MatchedCommands, StringComparer.OrdinalIgnoreCase);
            Assert.NotEmpty(broken.ParseErrors);

            var generic = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "GenericDemo.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.True(generic.ValidSyntax);
            Assert.Empty(generic.MatchedCommands);
            Assert.Contains("Get-UnrelatedThing", generic.Commands, StringComparer.OrdinalIgnoreCase);

            var reportRoot = Path.Combine(root, "_site", "api");
            var reportPath = WebApiDocsGenerator.WritePowerShellExampleValidationReport(
                reportRoot,
                null,
                validation,
                new WebApiDocsPowerShellExampleValidationOptions
                {
                    HelpPath = helpPath,
                    PowerShellExamplesPath = examplesDir,
                    TimeoutSeconds = 60
                });
            Assert.True(File.Exists(reportPath));

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.DoesNotContain(root, report.RootElement.GetProperty("helpPath").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Sample.Module-help.xml", report.RootElement.GetProperty("helpPath").GetString());
            Assert.Equal(1, report.RootElement.GetProperty("invalidSyntaxFileCount").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("unmatchedFileCount").GetInt32());
            Assert.Equal(3, report.RootElement.GetProperty("fileCount").GetInt32());
            var reportFiles = report.RootElement.GetProperty("files").EnumerateArray().ToArray();
            Assert.All(reportFiles, file =>
            {
                Assert.DoesNotContain(root, file.GetProperty("filePath").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.False(Path.IsPathRooted(file.GetProperty("filePath").GetString()));
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidatePowerShellExamples_ExecutesMatchedScripts_AndCapturesFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-execute-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                function Invoke-SampleTwo { param([string]$Name) "Ran two for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleTwo.Fail.ps1"), "Invoke-SampleTwo -Name 'Beta'; throw 'boom'");

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                TimeoutSeconds = 60,
                ExecuteMatchedExamples = true,
                ExecutionTimeoutSeconds = 60
            });

            Assert.True(validation.ValidationSucceeded);
            Assert.True(validation.ExecutionRequested);
            Assert.True(validation.ExecutionCompleted);
            Assert.Equal(2, validation.ExecutedFileCount);
            Assert.Equal(1, validation.PassedExecutionFileCount);
            Assert.Equal(1, validation.FailedExecutionFileCount);
            Assert.Contains(validation.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("failed execution", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleTwo.Fail.ps1", StringComparison.OrdinalIgnoreCase));

            var passing = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "Invoke-SampleOne.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.True(passing.ExecutionAttempted);
            Assert.True(passing.ExecutionSucceeded);
            Assert.Equal(0, passing.ExecutionExitCode);
            Assert.Contains("Ran one for Alpha", passing.ExecutionStdOut, StringComparison.Ordinal);

            var failing = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "Invoke-SampleTwo.Fail.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.True(failing.ExecutionAttempted);
            Assert.False(failing.ExecutionSucceeded);
            Assert.NotEqual(0, failing.ExecutionExitCode);
            Assert.Contains("boom", failing.ExecutionStdErr ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var reportRoot = Path.Combine(root, "_site", "api");
            var reportPath = WebApiDocsGenerator.WritePowerShellExampleValidationReport(
                reportRoot,
                null,
                validation,
                new WebApiDocsPowerShellExampleValidationOptions
                {
                    HelpPath = helpPath,
                    PowerShellExamplesPath = examplesDir,
                    TimeoutSeconds = 60,
                    ExecuteMatchedExamples = true,
                    ExecutionTimeoutSeconds = 60
                });
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.True(report.RootElement.GetProperty("executionRequested").GetBoolean());
            Assert.Equal(2, report.RootElement.GetProperty("executedFileCount").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("failedExecutionFileCount").GetInt32());
            Assert.DoesNotContain(root, report.RootElement.GetProperty("helpPath").GetString(), StringComparison.OrdinalIgnoreCase);

            var reportedFiles = report.RootElement.GetProperty("files").EnumerateArray().ToArray();
            var passingReport = Assert.Single(reportedFiles, file =>
                string.Equals(Path.GetFileName(file.GetProperty("filePath").GetString()), "Invoke-SampleOne.ps1", StringComparison.OrdinalIgnoreCase));
            var failingReport = Assert.Single(reportedFiles, file =>
                string.Equals(Path.GetFileName(file.GetProperty("filePath").GetString()), "Invoke-SampleTwo.Fail.ps1", StringComparison.OrdinalIgnoreCase));

            var passingArtifactPath = passingReport.GetProperty("executionArtifactPath").GetString();
            var failingArtifactPath = failingReport.GetProperty("executionArtifactPath").GetString();
            Assert.False(string.IsNullOrWhiteSpace(passingArtifactPath));
            Assert.False(string.IsNullOrWhiteSpace(failingArtifactPath));
            Assert.DoesNotContain(root, passingArtifactPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, failingArtifactPath, StringComparison.OrdinalIgnoreCase);
            Assert.False(Path.IsPathRooted(passingArtifactPath));
            Assert.False(Path.IsPathRooted(failingArtifactPath));
            var passingArtifactFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, passingArtifactPath!));
            var failingArtifactFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, failingArtifactPath!));
            Assert.True(File.Exists(passingArtifactFullPath));
            Assert.True(File.Exists(failingArtifactFullPath));
            Assert.Contains("File: Invoke-SampleOne.ps1", File.ReadAllText(passingArtifactFullPath), StringComparison.Ordinal);
            Assert.Contains("Ran one for Alpha", File.ReadAllText(passingArtifactFullPath), StringComparison.Ordinal);
            Assert.Contains("boom", File.ReadAllText(failingArtifactFullPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidatePowerShellExamples_WithExecutionRequestedAndNoScripts_MarksExecutionCompleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-execute-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                TimeoutSeconds = 60,
                ExecuteMatchedExamples = true,
                ExecutionTimeoutSeconds = 60
            });

            Assert.True(validation.ValidationSucceeded);
            Assert.True(validation.ExecutionRequested);
            Assert.True(validation.ExecutionCompleted);
            Assert.Equal(0, validation.FileCount);
            Assert.Equal(0, validation.ExecutedFileCount);
            Assert.Equal(0, validation.FailedExecutionFileCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidatePowerShellExamples_SkipsExecutionOutsideConfiguredExamplesPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-execute-rootguard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                """);

            var approvedExamplesDir = Path.Combine(root, "ApprovedExamples");
            Directory.CreateDirectory(approvedExamplesDir);
            File.WriteAllText(Path.Combine(approvedExamplesDir, "Invoke-SampleOne.Approved.ps1"), "Invoke-SampleOne -Name 'Approved'");

            var discoveredExamplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(discoveredExamplesDir);
            File.WriteAllText(Path.Combine(discoveredExamplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Discovered'");

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = approvedExamplesDir,
                TimeoutSeconds = 60,
                ExecuteMatchedExamples = true,
                ExecutionTimeoutSeconds = 60
            });

            var approved = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "Invoke-SampleOne.Approved.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.True(approved.ExecutionAttempted);
            Assert.True(approved.ExecutionSucceeded);

            var discovered = Assert.Single(
                validation.Files,
                file => string.Equals(Path.GetFileName(file.FilePath), "Invoke-SampleOne.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.False(discovered.ExecutionAttempted);
            Assert.Null(discovered.ExecutionSucceeded);
            Assert.Equal("Outside configured PowerShell examples path.", discovered.ExecutionSkippedReason);
            Assert.Contains(validation.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("outside the configured examples path", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.ps1", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_AttachesExecutionTranscriptMedia_ForSuccessfulImportedExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-execute-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                function Invoke-SampleTwo { param([string]$Name) "Ran two for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleTwo.Fail.ps1"), "Invoke-SampleTwo -Name 'Beta'; throw 'boom'");

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                TimeoutSeconds = 60,
                ExecuteMatchedExamples = true,
                ExecutionTimeoutSeconds = 60
            });

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            WebApiDocsGenerator.WritePowerShellExampleValidationReport(outputPath, null, validation);

            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                PowerShellExampleValidationResult = validation,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            using var sampleOneJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampleone.json")));
            var sampleOneExamples = sampleOneJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            var transcriptMedia = Assert.Single(sampleOneExamples, example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase) &&
                example.TryGetProperty("media", out var media) &&
                string.Equals(media.GetProperty("type").GetString(), "terminal", StringComparison.OrdinalIgnoreCase));
            var mediaUrl = transcriptMedia.GetProperty("media").GetProperty("url").GetString();
            Assert.Contains("/api/powershell/powershell-example-media/", mediaUrl, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(transcriptMedia.GetProperty("media").GetProperty("capturedAtUtc").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(transcriptMedia.GetProperty("media").GetProperty("sourceUpdatedAtUtc").GetString()));
            Assert.Contains("Open execution transcript", transcriptMedia.GetProperty("media").GetProperty("title").GetString(), StringComparison.Ordinal);

            var sampleOneHtml = File.ReadAllText(Path.Combine(outputPath, "invoke-sampleone", "index.html"));
            Assert.Contains("example-media example-media-terminal", sampleOneHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Open execution transcript", sampleOneHtml, StringComparison.Ordinal);
            Assert.Contains("Captured terminal transcript from executing Invoke-SampleOne.ps1.", sampleOneHtml, StringComparison.Ordinal);
            Assert.Contains("Captured ", sampleOneHtml, StringComparison.Ordinal);

            using var sampleTwoJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampletwo.json")));
            var sampleTwoExamples = sampleTwoJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            Assert.DoesNotContain(sampleTwoExamples, example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase) &&
                example.TryGetProperty("media", out var media) &&
                string.Equals(media.GetProperty("type").GetString(), "terminal", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_StagesExecutionTranscripts_WhenValidationReportLivesOutsideOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-transcript-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");

            var validation = WebApiDocsGenerator.ValidatePowerShellExamples(new WebApiDocsPowerShellExampleValidationOptions
            {
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                TimeoutSeconds = 60,
                ExecuteMatchedExamples = true,
                ExecutionTimeoutSeconds = 60
            });

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var reportPath = Path.Combine(root, "_reports", "powershell-example-validation.json");
            WebApiDocsGenerator.WritePowerShellExampleValidationReport(outputPath, reportPath, validation);

            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                PowerShellExampleValidationResult = validation,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            using var sampleJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampleone.json")));
            var sampleExamples = sampleJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            var transcriptMedia = Assert.Single(sampleExamples, example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase) &&
                example.TryGetProperty("media", out var media) &&
                string.Equals(media.GetProperty("type").GetString(), "terminal", StringComparison.OrdinalIgnoreCase));
            var mediaUrl = transcriptMedia.GetProperty("media").GetProperty("url").GetString();
            Assert.Contains("/api/powershell/powershell-example-media/", mediaUrl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_PrefersPlaybackSidecars_ForImportedExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-playback-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                function Invoke-SampleTwo { param([string]$Name) "Ran two for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.cast"), "dummy cast");
            File.WriteAllBytes(Path.Combine(examplesDir, "Invoke-SampleOne.png"), new byte[] { 1, 2, 3, 4 });

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both",
                CoverageReportPath = "reports/api-coverage.json"
            };

            var result = WebApiDocsGenerator.Generate(options);

            using var sampleOneJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampleone.json")));
            var sampleOneExamples = sampleOneJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            var playbackMedia = Assert.Single(sampleOneExamples, example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase) &&
                example.TryGetProperty("media", out var media) &&
                string.Equals(media.GetProperty("type").GetString(), "terminal", StringComparison.OrdinalIgnoreCase));
            var media = playbackMedia.GetProperty("media");
            var mediaUrl = media.GetProperty("url").GetString();
            var posterUrl = media.GetProperty("posterUrl").GetString();
            Assert.Contains("/api/powershell/powershell-example-media/", mediaUrl, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".cast", mediaUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/api/powershell/powershell-example-media/", posterUrl, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".png", posterUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("application/x-asciicast", media.GetProperty("mimeType").GetString());
            Assert.False(string.IsNullOrWhiteSpace(media.GetProperty("capturedAtUtc").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(media.GetProperty("sourceUpdatedAtUtc").GetString()));
            Assert.Contains("Open terminal playback", media.GetProperty("title").GetString(), StringComparison.Ordinal);

            var castFileName = Path.GetFileName(new Uri("https://example.test" + mediaUrl).LocalPath);
            var posterFileName = Path.GetFileName(new Uri("https://example.test" + posterUrl).LocalPath);
            Assert.True(File.Exists(Path.Combine(outputPath, "powershell-example-media", castFileName)));
            Assert.True(File.Exists(Path.Combine(outputPath, "powershell-example-media", posterFileName)));
            Assert.False(string.IsNullOrWhiteSpace(result.PowerShellExampleMediaManifestPath));
            Assert.True(File.Exists(result.PowerShellExampleMediaManifestPath!));

            using var mediaManifest = JsonDocument.Parse(File.ReadAllText(result.PowerShellExampleMediaManifestPath!));
            Assert.Equal(1, mediaManifest.RootElement.GetProperty("entryCount").GetInt32());
            var manifestEntry = Assert.Single(mediaManifest.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("Invoke-SampleOne", manifestEntry.GetProperty("commandName").GetString());
            Assert.Equal("Invoke-SampleOne.ps1", manifestEntry.GetProperty("sourcePath").GetString());
            Assert.Equal("Invoke-SampleOne.cast", manifestEntry.GetProperty("assetPath").GetString());
            Assert.Equal("Invoke-SampleOne.png", manifestEntry.GetProperty("posterAssetPath").GetString());
            Assert.True(manifestEntry.GetProperty("hasPoster").GetBoolean());

            var sampleOneHtml = File.ReadAllText(Path.Combine(outputPath, "invoke-sampleone", "index.html"));
            Assert.Contains("Open terminal playback", sampleOneHtml, StringComparison.Ordinal);
            Assert.Contains("Captured terminal playback for Invoke-SampleOne.ps1.", sampleOneHtml, StringComparison.Ordinal);
            Assert.Contains("example-media-poster", sampleOneHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Captured ", sampleOneHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_DeduplicatesPlaybackMediaPerImportedScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-playback-dedupe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"),
                """
                Invoke-SampleOne -Name 'Alpha'
                Invoke-SampleOne -Name 'Beta'
                """);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.cast"), "dummy cast");
            File.WriteAllBytes(Path.Combine(examplesDir, "Invoke-SampleOne.png"), new byte[] { 1, 2, 3, 4 });

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);

            using var sampleOneJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampleone.json")));
            var sampleOneExamples = sampleOneJson.RootElement.GetProperty("examples").EnumerateArray().ToArray();
            Assert.Equal(2, sampleOneExamples.Count(example =>
                string.Equals(example.GetProperty("kind").GetString(), "code", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(example.GetProperty("origin").GetString(), "ImportedScript", StringComparison.OrdinalIgnoreCase)));
            Assert.Single(sampleOneExamples, example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase) &&
                example.TryGetProperty("media", out var media) &&
                string.Equals(media.GetProperty("type").GetString(), "terminal", StringComparison.OrdinalIgnoreCase));

            Assert.False(string.IsNullOrWhiteSpace(result.PowerShellExampleMediaManifestPath));
            using var mediaManifest = JsonDocument.Parse(File.ReadAllText(result.PowerShellExampleMediaManifestPath!));
            Assert.Equal(1, mediaManifest.RootElement.GetProperty("entryCount").GetInt32());
            var manifestEntry = Assert.Single(mediaManifest.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("Invoke-SampleOne", manifestEntry.GetProperty("commandName").GetString());
            Assert.Equal("Invoke-SampleOne.cast", manifestEntry.GetProperty("assetPath").GetString());

            var sampleOneHtml = File.ReadAllText(Path.Combine(outputPath, "invoke-sampleone", "index.html"));
            Assert.Single(Regex.Matches(sampleOneHtml, "Open terminal playback", RegexOptions.IgnoreCase).Cast<Match>());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_UsesParentDirectoryWhenPowerShellExamplesPathPointsToSingleFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-single-file-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            var examplePath = Path.Combine(examplesDir, "Invoke-SampleOne.ps1");
            File.WriteAllText(examplePath, "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.cast"), "dummy cast");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplePath,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);

            Assert.False(string.IsNullOrWhiteSpace(result.PowerShellExampleMediaManifestPath));
            using var mediaManifest = JsonDocument.Parse(File.ReadAllText(result.PowerShellExampleMediaManifestPath!));
            var manifestEntry = Assert.Single(mediaManifest.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("Invoke-SampleOne.ps1", manifestEntry.GetProperty("sourcePath").GetString());
            Assert.Equal("Invoke-SampleOne.cast", manifestEntry.GetProperty("assetPath").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_RestagesPlaybackMediaWhenSourceTimestampChangesBackward()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-restage-playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            var examplePath = Path.Combine(examplesDir, "Invoke-SampleOne.ps1");
            var castPath = Path.Combine(examplesDir, "Invoke-SampleOne.cast");
            File.WriteAllText(examplePath, "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(castPath, "newer capture");
            File.SetLastWriteTimeUtc(castPath, DateTime.UtcNow.AddMinutes(5));

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            WebApiDocsGenerator.Generate(options);

            File.WriteAllText(castPath, "older but current capture");
            File.SetLastWriteTimeUtc(castPath, DateTime.UtcNow.AddMinutes(-30));

            WebApiDocsGenerator.Generate(options);

            var stagedCast = Directory.GetFiles(Path.Combine(outputPath, "powershell-example-media"), "*.cast").Single();
            Assert.Equal("older but current capture", File.ReadAllText(stagedCast));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_WarnsWhenPlaybackAssetsAreOversizedStaleOrUnsupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-playback-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                function Invoke-SampleTwo { param([string]$Name) "Ran two for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);

            var examplePath = Path.Combine(examplesDir, "Invoke-SampleOne.ps1");
            var castPath = Path.Combine(examplesDir, "Invoke-SampleOne.cast");
            var posterPath = Path.Combine(examplesDir, "Invoke-SampleOne.png");
            var unsupportedPath = Path.Combine(examplesDir, "Invoke-SampleOne.gif");

            File.WriteAllText(examplePath, "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllBytes(castPath, new byte[(2 * 1024 * 1024) + 128]);
            File.WriteAllBytes(posterPath, new byte[(1024 * 1024) + 128]);
            File.WriteAllBytes(unsupportedPath, new byte[] { 1, 2, 3, 4 });

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(castPath, now.AddMinutes(-20));
            File.SetLastWriteTimeUtc(posterPath, now.AddMinutes(-30));
            File.SetLastWriteTimeUtc(examplePath, now.AddMinutes(-5));

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both"
            };

            var result = WebApiDocsGenerator.Generate(options);

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("unsupported playback sidecar", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.gif", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("playback sidecar", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("recommended 2 MiB", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.cast", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("playback poster", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("recommended 1 MiB", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.png", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("looks stale", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.cast", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("looks stale", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleOne.png", StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(result.PowerShellExampleMediaManifestPath));

            using var sampleOneJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "invoke-sampleone.json")));
            var staleMedia = Assert.Single(sampleOneJson.RootElement.GetProperty("examples").EnumerateArray(), example =>
                string.Equals(example.GetProperty("kind").GetString(), "media", StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(staleMedia.GetProperty("media").GetProperty("capturedAtUtc").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(staleMedia.GetProperty("media").GetProperty("sourceUpdatedAtUtc").GetString()));

            var sampleOneHtml = File.ReadAllText(Path.Combine(outputPath, "invoke-sampleone", "index.html"));
            Assert.Contains("Script changed after this capture", sampleOneHtml, StringComparison.Ordinal);

            using var mediaManifest = JsonDocument.Parse(File.ReadAllText(result.PowerShellExampleMediaManifestPath!));
            var manifestEntry = Assert.Single(mediaManifest.RootElement.GetProperty("entries").EnumerateArray());
            Assert.True(manifestEntry.GetProperty("hasUnsupportedSidecars").GetBoolean());
            Assert.True(manifestEntry.GetProperty("hasOversizedAssets").GetBoolean());
            Assert.True(manifestEntry.GetProperty("hasStaleAssets").GetBoolean());

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var powershell = coverage.RootElement.GetProperty("powershell");
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMedia").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMediaUnsupportedSidecars").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMediaOversizedAssets").GetProperty("covered").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMediaStaleAssets").GetProperty("covered").GetInt32());
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleOne", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMediaOversizedAssets").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleOne", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMediaStaleAssets").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleOne", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_ReportsPlaybackCoverage_AndWarnsWhenPosterIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-powershell-playback-coverage-" + Guid.NewGuid().ToString("N"));
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
                      <command:name>Invoke-SampleOne</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Runs the first sample.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Invoke-SampleOne</command:name>
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
                  <command:command>
                    <command:details>
                      <command:name>Invoke-SampleTwo</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Runs the second sample.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Invoke-SampleTwo</command:name>
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
                    FunctionsToExport = @('Invoke-SampleOne', 'Invoke-SampleTwo')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleOne { param([string]$Name) "Ran one for $Name" }
                function Invoke-SampleTwo { param([string]$Name) "Ran two for $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.ps1"), "Invoke-SampleOne -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleOne.cast"), "dummy cast one");
            File.WriteAllBytes(Path.Combine(examplesDir, "Invoke-SampleOne.png"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleTwo.ps1"), "Invoke-SampleTwo -Name 'Beta'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleTwo.cast"), "dummy cast two");

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                PowerShellExamplesPath = examplesDir,
                OutputPath = outputPath,
                Title = "PowerShell API",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "both",
                CoverageReportPath = "reports/api-coverage.json"
            };

            var result = WebApiDocsGenerator.Generate(options);

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.POWERSHELL]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("without poster art", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("Invoke-SampleTwo", StringComparison.OrdinalIgnoreCase));

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var powershell = coverage.RootElement.GetProperty("powershell");
            Assert.Equal(2, powershell.GetProperty("importedScriptPlaybackMedia").GetProperty("covered").GetInt32());
            Assert.Equal(2, powershell.GetProperty("importedScriptPlaybackMedia").GetProperty("total").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMediaWithPoster").GetProperty("covered").GetInt32());
            Assert.Equal(2, powershell.GetProperty("importedScriptPlaybackMediaWithPoster").GetProperty("total").GetInt32());
            Assert.Equal(1, powershell.GetProperty("importedScriptPlaybackMediaWithoutPoster").GetProperty("covered").GetInt32());
            Assert.Equal(2, powershell.GetProperty("importedScriptPlaybackMediaWithoutPoster").GetProperty("total").GetInt32());
            Assert.Equal(0, powershell.GetProperty("importedScriptPlaybackMediaUnsupportedSidecars").GetProperty("covered").GetInt32());
            Assert.Equal(0, powershell.GetProperty("importedScriptPlaybackMediaOversizedAssets").GetProperty("covered").GetInt32());
            Assert.Equal(0, powershell.GetProperty("importedScriptPlaybackMediaStaleAssets").GetProperty("covered").GetInt32());

            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMedia").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleOne", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMedia").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleTwo", StringComparison.Ordinal));
            Assert.Contains(
                powershell.GetProperty("commandsUsingImportedScriptPlaybackMediaWithoutPoster").EnumerateArray().Select(x => x.GetString()),
                value => string.Equals(value, "Invoke-SampleTwo", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalPowerShellHelpForValidation()
    {
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleOne</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Runs the first sample.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleOne</command:name>
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
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleTwo</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Runs the second sample.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleTwo</command:name>
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
            """;
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        RunGit(workingDirectory, null, args);
    }

    private static void RunGit(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start git.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}{Environment.NewLine}{stdout}");
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
            // best-effort cleanup for git temp repos on Windows
        }
    }
}
