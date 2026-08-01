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
    AliasesToExport = @('GetText', 'TestPublicEmptyArraySingleton')
    VariablesToExport = @('collectorProtocol')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "HelperIsolationFixture.psm1"), """
$collectorProtocol = [pscustomobject]@{
    RemoveHelperAliases = { throw 'The target module clobbered the collector protocol.' }
    HelperFunctionNames = @('clobbered')
}

function ConvertToUtf16CodeUnits {
    throw 'The target module clobbered a documentation collector helper.'
}

Set-Alias -Name GetText -Value ConvertToUtf16CodeUnits
Set-Alias -Name TestPublicEmptyArraySingleton -Value ConvertToUtf16CodeUnits

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
        $valueAlias = [System.Management.Automation.AliasAttribute]::new(
            [string[]]@([string][char]0xD800, ' x '))
        $valueAttributes.Add($valueAlias)
        $parameters.Add('Value', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'Value', [string], $valueAttributes))

        $helpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $helpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $helpDefault.Help = "authored`nvalue"
        $helpDefault.Value = 'ignored'
        $helpAttributes.Add($helpDefault)
        $parameters.Add('HelpValue', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'HelpValue', [string], $helpAttributes))

        $emptyArrayAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $emptyArrayDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $emptyArrayDefault.Value = [System.Array].GetMethod('Empty').MakeGenericMethod([int]).Invoke(
            $null, [object[]]@())
        $emptyArrayAttributes.Add($emptyArrayDefault)
        $parameters.Add('EmptyArray', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'EmptyArray', [int[]], $emptyArrayAttributes))

        $parameters
    }
}

Export-ModuleMember -Function Get-HelperIsolationFixture, ConvertToUtf16CodeUnits `
    -Alias GetText, TestPublicEmptyArraySingleton -Variable collectorProtocol
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
                var valueParameter = Assert.Single(command.Parameters, parameter => parameter.Name == "Value");
                Assert.Equal("'runtime value'", Default("Value"));
                Assert.Equal(["([char]55296)", " x "], valueParameter.Aliases);
                Assert.Equal("authored\nvalue", Default("HelpValue"));
                Assert.StartsWith(
                    "& { return ,([System.Array].GetMethod('Empty'",
                    Default("EmptyArray"),
                    StringComparison.Ordinal);
                Assert.Contains(command.Outputs, output =>
                    output.CanonicalTypeName == "System.Collections.Generic.List[System.String]");

                var hostOutput = Path.Combine(root, host.Replace('.', '-'));
                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                    payload,
                    "HelperIsolationFixture",
                    hostOutput);
                var maml = File.ReadAllText(mamlPath);
                Assert.Contains("([char]55296)", maml, StringComparison.Ordinal);
                Assert.DoesNotContain('\uD800', maml);

                var aliasAttributes = System.Xml.Linq.XDocument.Load(
                        mamlPath,
                        System.Xml.Linq.LoadOptions.PreserveWhitespace)
                    .Descendants()
                    .Where(element => element.Name.LocalName == "parameter")
                    .Select(element => element.Attribute("aliases")?.Value)
                    .Where(value => value is not null && value != "none")
                    .ToArray();
                Assert.Equal(2, aliasAttributes.Length);
                Assert.All(aliasAttributes, value => Assert.Equal("([char]55296), x ", value));

                var docsPath = Path.Combine(hostOutput, "Docs");
                new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "HelperIsolationFixture", docsPath);
                var markdown = File.ReadAllText(Path.Combine(docsPath, "Get-HelperIsolationFixture.md"));
                Assert.Contains("Aliases: ([char]55296), ' x '", markdown, StringComparison.Ordinal);

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
