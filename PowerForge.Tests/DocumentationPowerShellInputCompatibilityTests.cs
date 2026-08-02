using System.Text;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void DocumentationEngine_SplitsAggregatedInputTypesAcrossPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "InputFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'InputFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '88888888-8888-8888-8888-888888888888'
    FunctionsToExport = @('Install-InputFixture', 'Get-AuthoredInputFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "InputFixture.psm1"), """
function Install-InputFixture {
    [CmdletBinding(DefaultParameterSetName = 'ByName')]
    param(
        [Parameter(ValueFromPipeline = $true, ParameterSetName = 'ByName')]
        [string[]] $Name,

        [Parameter(ValueFromPipeline = $true, ParameterSetName = 'ByInfo')]
        [psobject[]] $Info
    )

    process { }
}

function Get-AuthoredInputFixture {
    <#
    .SYNOPSIS
    Gets an authored input fixture.

    .INPUTS
    System.String
    This is string input.
    #>
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string] $InputObject
    )

    process { }
}

Export-ModuleMember -Function Install-InputFixture, Get-AuthoredInputFixture
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(payload.Commands, item => item.Name == "Install-InputFixture");

                Assert.Collection(
                    command.Inputs,
                    input => Assert.Equal("System.String[]", input.Name),
                    input => Assert.Equal("System.Management.Automation.PSObject[]", input.Name));

                var docsPath = Path.Combine(root, "Docs-" + Path.GetFileNameWithoutExtension(host));
                new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "InputFixture", docsPath);
                var markdown = File.ReadAllText(Path.Combine(docsPath, "Install-InputFixture.md"));
                Assert.Contains("- `System.String[]`\r\n", markdown, StringComparison.Ordinal);
                Assert.Contains("- `System.Management.Automation.PSObject[]`\r\n", markdown, StringComparison.Ordinal);
                Assert.DoesNotContain("%u000D", markdown, StringComparison.Ordinal);
                Assert.DoesNotContain("%u000A", markdown, StringComparison.Ordinal);

                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, "InputFixture", root);
                var maml = XDocument.Load(mamlPath);
                var installCommand = maml.Descendants()
                    .Single(element =>
                        element.Name.LocalName == "command" &&
                        element.Descendants().Any(child =>
                            child.Name.LocalName == "name" &&
                            child.Value == "Install-InputFixture"));
                var inputNames = installCommand.Descendants()
                    .Where(element => element.Name.LocalName == "inputType")
                    .Select(element => element.Descendants().First(child => child.Name.LocalName == "name").Value)
                    .ToArray();
                Assert.Equal(
                    new[] { "System.String[]", "System.Management.Automation.PSObject[]" },
                    inputNames);

                var authored = Assert.Single(payload.Commands, item => item.Name == "Get-AuthoredInputFixture");
                var authoredInput = Assert.Single(authored.Inputs);
                Assert.Equal("System.String", authoredInput.Name);
                Assert.Equal("This is string input.", authoredInput.Description);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
