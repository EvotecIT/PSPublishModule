using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void DocumentationEngine_IsolatesCollectorHelpersFromTargetModuleExports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-helper-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "HelperIsolationFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'HelperIsolationFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '12121212-1212-1212-1212-121212121212'
    FunctionsToExport = @('Get-HelperIsolationFixture', 'ConvertToUtf16CodeUnits')
    AliasesToExport = @('GetText')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "HelperIsolationFixture.psm1"), """
function ConvertToUtf16CodeUnits {
    throw 'The target module clobbered a documentation collector helper.'
}

Set-Alias -Name GetText -Value ConvertToUtf16CodeUnits

function Get-HelperIsolationFixture {
    [CmdletBinding()]
    [OutputType([System.Collections.Generic.List[string]])]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()

        $valueAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $valueDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $valueDefault.Value = 'runtime value'
        $valueAttributes.Add($valueDefault)
        $parameters.Add('Value', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'Value', [string], $valueAttributes))

        $helpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $helpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $helpDefault.Help = "authored`nvalue"
        $helpDefault.Value = 'ignored'
        $helpAttributes.Add($helpDefault)
        $parameters.Add('HelpValue', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'HelpValue', [string], $helpAttributes))

        $parameters
    }
}

Export-ModuleMember -Function Get-HelperIsolationFixture, ConvertToUtf16CodeUnits -Alias GetText
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var payload = new DocumentationEngine(
                        new ExecutablePowerShellRunner(host, root),
                        new NullLogger())
                    .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(payload.Commands, candidate =>
                    candidate.Name == "Get-HelperIsolationFixture");
                Assert.Equal("'runtime value'", Default("Value"));
                Assert.Equal("authored\nvalue", Default("HelpValue"));
                Assert.Contains(command.Outputs, output =>
                    output.CanonicalTypeName == "System.Collections.Generic.List[System.String]");

                string Default(string name)
                    => Assert.Single(command.Parameters, parameter => parameter.Name == name).DefaultValue;
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }
}
