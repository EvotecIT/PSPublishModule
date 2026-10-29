using System.Text;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentationEngine_DynamicHelpWarnsUntilDiscoveryContextVerifiesParameters(bool registeredScriptBlock)
    {
        string root = Path.Combine(Path.GetTempPath(), "pf-dynamic-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "en-US"));
        Directory.CreateDirectory(Path.Combine(root, "provider-context"));
        try
        {
            const string moduleName = "DynamicParameterFixture";
            string manifest = Path.Combine(root, moduleName + ".psd1");
            File.WriteAllText(manifest, """
@{
    RootModule = 'DynamicParameterFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '4112a937-eec3-420e-acb0-fc1e8e459555'
    FunctionsToExport = @('Get-DynamicParameterFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""");
            string source = """
function Get-DynamicParameterFixture {
    <#
    .EXTERNALHELP DynamicParameterFixture-help.xml
    #>
    [CmdletBinding()]
    param([string] $Mode)
    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        if ($Mode -in @('enabled', 'different', 'differentdefault', 'textdefault', 'unsupporteddefault') -or (Split-Path -Leaf (Get-Location).Path) -eq 'provider-context') {
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $attributes.Add([System.Management.Automation.ParameterAttribute]::new())
            $value = if ($Mode -eq 'different') { 'a' } else { 'A' }
            $validate = [System.Management.Automation.ValidateSetAttribute]::new([string[]]@($value))
            $validate.IgnoreCase = $false
            $attributes.Add($validate)
            $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
            if ($Mode -in @('textdefault', 'unsupporteddefault')) {
                if ($Mode -eq 'textdefault') { $default.Help = 'Documented default' }
                $default.Value = [IntPtr]::new(4294967296)
            } else {
                $default.Value = if ($Mode -eq 'differentdefault') { [string[]]@('one', 'changed') } else { [string[]]@('one', 'two') }
            }
            $attributes.Add($default)
            $parameters.Add('Live', [System.Management.Automation.RuntimeDefinedParameter]::new('Live', [string], $attributes))
        }
        $parameters
    }
}
Export-ModuleMember -Function Get-DynamicParameterFixture
""";
            if (registeredScriptBlock)
                source = source.Replace("function Get-DynamicParameterFixture {", "Set-Item -Path function:Get-DynamicParameterFixture -Value {", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), source);
            var oldHelp = new DocumentationExtractionPayload
            {
                ModuleName = moduleName,
                Commands = new() { new DocumentationCommandHelp
                {
                    Name = "Get-DynamicParameterFixture", CommandType = "Function", Synopsis = "Get a fixture.",
                    Parameters = new()
                    {
                        new() { Name = "Mode", Type = "String", Description = "Mode description." },
                        new() { Name = "Live", Type = "String", Description = "Live description." },
                        new() { Name = "Removed", Type = "String", Description = "Old description." }
                    }
                } }
            };
            new MamlHelpWriter().WriteExternalHelpFile(oldHelp, moduleName, Path.Combine(root, "en-US"));
            string[] hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
            foreach (string host in hosts)
            {
                new MamlHelpWriter().WriteExternalHelpFile(oldHelp, moduleName, Path.Combine(root, "en-US"));
                var logger = new BufferedLogger();
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), logger);
                DocumentationExtractionPayload unverifiedPayload = engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1));
                DocumentationCommandHelp unverified = Assert.Single(unverifiedPayload.Commands);
                Assert.Contains(unverified.Parameters, parameter => parameter.Name == "Live");
                Assert.Contains(unverified.Parameters, parameter => parameter.Name == "Removed");
                BufferedLogEntry warning = Assert.Single(logger.Entries, entry => entry.Level == "warn");
                Assert.Contains("Get-DynamicParameterFixture", warning.Message, StringComparison.Ordinal);
                Assert.Contains("Live", warning.Message, StringComparison.Ordinal);
                Assert.Contains("Removed", warning.Message, StringComparison.Ordinal);

                logger.Entries.Clear();
                var buildSettings = new BuildDocumentationConfiguration { Enable = true, IncludeAboutTopics = false, GenerateFallbackExamples = false };
                DocumentationBuildResult unverifiedBuild = engine.Build(moduleName, root, manifest,
                    new DocumentationConfiguration { Path = Path.Combine(root, "build-docs-" + Path.GetFileNameWithoutExtension(host)) }, buildSettings);
                Assert.True(unverifiedBuild.Succeeded, unverifiedBuild.ErrorMessage);
                Assert.Single(logger.Entries, entry => entry.Level == "warn");
                string unverifiedMaml = File.ReadAllText(unverifiedBuild.ExternalHelpFilePath);
                Assert.Contains("Removed", unverifiedMaml, StringComparison.Ordinal);
                Assert.Contains("Old description.", unverifiedMaml, StringComparison.Ordinal);

                logger.Entries.Clear();
                var contexts = new[] { new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "enabled" } } };
                DocumentationCommandHelp verified = Assert.Single(engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), contexts).Commands);
                Assert.Contains(verified.Parameters, parameter => parameter.Name == "Live" && parameter.Type == "String");
                Assert.DoesNotContain(verified.Parameters, parameter => parameter.Name == "Removed");
                Assert.DoesNotContain("unverified", Assert.Single(verified.Parameters, parameter => parameter.Name == "Live").Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(logger.Entries, entry => entry.Level == "warn");

                buildSettings.ParameterDiscoveryContexts = contexts;
                DocumentationBuildResult verifiedBuild = engine.Build(moduleName, root, manifest,
                    new DocumentationConfiguration { Path = Path.Combine(root, "build-docs-" + Path.GetFileNameWithoutExtension(host)) }, buildSettings);
                Assert.True(verifiedBuild.Succeeded, verifiedBuild.ErrorMessage);
                string verifiedMaml = File.ReadAllText(verifiedBuild.ExternalHelpFilePath);
                Assert.Contains("Live", verifiedMaml, StringComparison.Ordinal);
                Assert.Contains("Live description.", verifiedMaml, StringComparison.Ordinal);
                Assert.DoesNotContain("Removed", verifiedMaml, StringComparison.Ordinal);

                var providerContexts = new[] { new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", ProviderPath = Path.Combine(root, "provider-context") } };
                DocumentationCommandHelp providerVerified = Assert.Single(engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), providerContexts).Commands);
                Assert.Contains(providerVerified.Parameters, parameter => parameter.Name == "Live" && parameter.Type == "String");
                Assert.DoesNotContain(providerVerified.Parameters, parameter => parameter.Name == "Removed");

                var multipleContexts = new[]
                {
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "disabled" } },
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "enabled" } },
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "enabled" } }
                };
                DocumentationExtractionPayload multiple = engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), multipleContexts);
                DocumentationCommandHelp multipleCommand = Assert.Single(multiple.Commands);
                Assert.Equal(2, multipleCommand.Syntax.Count);
                string mamlPath = new MamlHelpWriter().WriteExternalHelpFile(multiple, moduleName, Path.Combine(root, "multiple-" + Path.GetFileNameWithoutExtension(host)));
                XDocument maml = XDocument.Load(mamlPath);
                var syntaxItems = maml.Descendants().Where(element => element.Name.LocalName == "syntaxItem").ToArray();
                Assert.Equal(2, syntaxItems.Length);
                Assert.Single(syntaxItems, item => item.Descendants().Any(element => element.Name.LocalName == "name" && element.Value == "Live"));

                var conflictingContexts = new[]
                {
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "enabled" } },
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "different" } }
                };
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), conflictingContexts));
                Assert.Contains("Conflicting parameter metadata", error.Message, StringComparison.Ordinal);

                var textDefaultContexts = new[]
                {
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "textdefault" } },
                    new DocumentationDiscoveryContext { Command = "Get-DynamicParameterFixture", Arguments = new[] { "textdefault" } }
                };
                DocumentationCommandHelp textDefault = Assert.Single(engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), textDefaultContexts).Commands);
                Assert.Equal("Documented default", Assert.Single(textDefault.Parameters, parameter => parameter.Name == "Live").DefaultValue);
                foreach (DocumentationDiscoveryContext context in textDefaultContexts)
                    context.Arguments = new[] { "unsupporteddefault" };
                DocumentationCommandHelp unsupportedDefault = Assert.Single(engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), textDefaultContexts).Commands);
                Assert.Contains(unsupportedDefault.Parameters, parameter => parameter.Name == "Live");

                conflictingContexts[1].Arguments = new[] { "differentdefault" };
                error = Assert.Throws<InvalidOperationException>(() => engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1), conflictingContexts));
                Assert.Contains("Conflicting parameter metadata", error.Message, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DocumentationEngine_RegeneratedHelpUsesCurrentParametersAndPreservesTheirDescriptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "pf-stale-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "en-US"));
        try
        {
            const string moduleName = "StaleParameterFixture";
            string manifest = Path.Combine(root, moduleName + ".psd1");
            File.WriteAllText(manifest, """
@{
    RootModule = 'StaleParameterFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = 'aee6a425-6cbd-49ba-a8a9-0f8273f1b372'
    FunctionsToExport = @('Get-StaleParameterFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), """
function Get-StaleParameterFixture {
    <#
    .EXTERNALHELP StaleParameterFixture-help.xml
    #>
    [CmdletBinding()]
    param([string] $Current, [string] $Added)
}
Export-ModuleMember -Function Get-StaleParameterFixture
""", new UTF8Encoding(false));

            var oldHelp = new DocumentationExtractionPayload
            {
                ModuleName = moduleName,
                Commands = new()
                {
                    new DocumentationCommandHelp
                    {
                        Name = "Get-StaleParameterFixture", CommandType = "Function", Synopsis = "Get a fixture.",
                        Parameters = new()
                        {
                            new() { Name = "Current", Type = "String", Description = "Current description." },
                            new() { Name = "Removed", Type = "String", Description = "Old parameter description." }
                        }
                    }
                }
            };
            new MamlHelpWriter().WriteExternalHelpFile(oldHelp, moduleName, Path.Combine(root, "en-US"));

            string[] hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
            foreach (string host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                DocumentationExtractionPayload payload = engine.ExtractHelpPayload(root, manifest, TimeSpan.FromMinutes(1));
                DocumentationCommandHelp command = Assert.Single(payload.Commands);
                Assert.Equal(new[] { "Added", "Current" }, command.Parameters.Select(parameter => parameter.Name).OrderBy(name => name).ToArray());
                Assert.Contains("Current description.", Assert.Single(command.Parameters, parameter => parameter.Name == "Current").Description, StringComparison.Ordinal);

                string docs = Path.Combine(root, "docs-" + Path.GetFileNameWithoutExtension(host));
                new MarkdownHelpWriter().WriteCommandHelpFiles(payload, moduleName, docs);
                string markdown = File.ReadAllText(Path.Combine(docs, "Get-StaleParameterFixture.md"));
                Assert.DoesNotContain("-Removed", markdown, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("-Current", markdown, StringComparison.OrdinalIgnoreCase);
                string mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, moduleName, Path.Combine(root, "generated-" + Path.GetFileNameWithoutExtension(host)));
                XDocument maml = XDocument.Load(mamlPath);
                string[] names = maml.Descendants().Where(element => element.Name.LocalName == "parameter")
                    .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "name").Select(child => child.Value)).ToArray();
                Assert.Contains("Current", names, StringComparer.OrdinalIgnoreCase);
                Assert.DoesNotContain("Removed", names, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
