using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationScriptFixtureTests
{
    [Fact]
    public void DocumentationEngine_PreservesScriptOutputDescriptionsWithRuntimeOutputTypes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-docs-output-description-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string moduleName = "DescribedOutputModule";
            var modulePath = Path.Combine(tempRoot, moduleName + ".psm1");
            var manifestPath = Path.Combine(tempRoot, moduleName + ".psd1");

            File.WriteAllText(modulePath, """
function Get-DescribedOutput {
    <#
    .EXTERNALHELP DescribedOutputModule-help.xml
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Generic.List[string]])]
    param()

    [System.Collections.Generic.List[string]]::new()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var helpDirectory = Path.Combine(tempRoot, "en-US");
            Directory.CreateDirectory(helpDirectory);
            File.WriteAllText(Path.Combine(helpDirectory, moduleName + "-help.xml"), """
<?xml version="1.0" encoding="utf-8"?>
<helpItems schema="maml" xmlns="http://msh">
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-DescribedOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>DescribedOutput</command:noun>
      <maml:description>
        <maml:para>Returns a described output value.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Returns a described output value.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-DescribedOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>list[system.string]</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>A generic list whose authored output description must survive metadata extraction.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
</helpItems>
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(manifestPath, """
@{
    RootModule = 'DescribedOutputModule.psm1'
    ModuleVersion = '1.0.0'
    GUID = '88888888-8888-8888-8888-888888888888'
    Author = 'PowerForge.Tests'
    Description = 'Script fixture module for output-description extraction tests.'
    FunctionsToExport = @('Get-DescribedOutput')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var payload = engine.ExtractHelpPayload(tempRoot, manifestPath, TimeSpan.FromMinutes(1));
            var output = Assert.Single(Assert.Single(payload.Commands).Outputs);
            Assert.StartsWith("System.Collections.Generic.List`1[[System.String,", output.ClrTypeName, StringComparison.Ordinal);
            Assert.Contains(
                "authored output description must survive metadata extraction",
                output.Description,
                StringComparison.Ordinal);

            var markdownDirectory = Path.Combine(tempRoot, "GeneratedDocs");
            var mamlDirectory = Path.Combine(tempRoot, "GeneratedHelp");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, moduleName, markdownDirectory);
            var generatedMamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, moduleName, mamlDirectory);
            Assert.Contains(
                "authored output description must survive metadata extraction",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-DescribedOutput.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "authored output description must survive metadata extraction",
                File.ReadAllText(generatedMamlPath),
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    [Fact]
    public void DocumentationEngine_HandlesCommandParameterNamedKeys()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-docs-keys-parameter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string moduleName = "KeyCollisionModule";
            var modulePath = Path.Combine(tempRoot, moduleName + ".psm1");
            var manifestPath = Path.Combine(tempRoot, moduleName + ".psd1");

            File.WriteAllText(modulePath, """
function Invoke-KeyCollision {
    <#
    .SYNOPSIS
    Invokes a documentation extraction fixture.

    .PARAMETER Keys
    Key names that exercise dictionary key/member collision handling.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Keys
    )

    $Keys
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(manifestPath, """
@{
    RootModule = 'KeyCollisionModule.psm1'
    ModuleVersion = '1.0.0'
    GUID = '77777777-7777-7777-7777-777777777777'
    Author = 'PowerForge.Tests'
    Description = 'Script fixture module for documentation extraction key collision tests.'
    FunctionsToExport = @('Invoke-KeyCollision')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var result = engine.Build(
                moduleName: moduleName,
                stagingPath: tempRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = "Docs",
                    PathReadme = Path.Combine("Docs", "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true,
                    GenerateExternalHelp = true,
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = true
                });

            Assert.True(result.Succeeded, result.ErrorMessage);

            var markdownPath = Path.Combine(tempRoot, "Docs", "Invoke-KeyCollision.md");
            Assert.True(File.Exists(markdownPath), $"Expected generated markdown help at '{markdownPath}'.");

            var markdown = File.ReadAllText(markdownPath);
            Assert.Contains("### -Keys", markdown);
            Assert.Contains("Key names that exercise dictionary key/member collision handling.", markdown);
            Assert.DoesNotContain("ParameterType", markdown);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }
}
