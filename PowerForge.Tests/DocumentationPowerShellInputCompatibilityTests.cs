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
    FunctionsToExport = @('Install-InputFixture')
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

Export-ModuleMember -Function Install-InputFixture
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(payload.Commands);

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
                var inputNames = maml.Descendants()
                    .Where(element => element.Name.LocalName == "inputType")
                    .Select(element => element.Descendants().First(child => child.Name.LocalName == "name").Value)
                    .ToArray();
                Assert.Equal(
                    new[] { "System.String[]", "System.Management.Automation.PSObject[]" },
                    inputNames);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
