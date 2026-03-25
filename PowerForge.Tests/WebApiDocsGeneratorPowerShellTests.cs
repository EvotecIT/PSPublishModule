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
            Assert.Contains("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
            Assert.Contains("prism-core.min.js", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prism-autoloader.min.js", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("plugins.autoloader.languages_path", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("setTimeout(run,delayMs)", html, StringComparison.OrdinalIgnoreCase);

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
            if (Directory.Exists(root))
                Directory.Delete(root, true);
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

            WebApiDocsGenerator.Generate(options);
            var html = File.ReadAllText(Path.Combine(outputPath, "get-sampledata", "index.html"));

            Assert.Contains("language-powershell", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prism-core", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prism-autoloader", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("plugins.autoloader.languages_path", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
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
}
