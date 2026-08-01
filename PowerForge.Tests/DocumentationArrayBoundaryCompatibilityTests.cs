using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationArrayBoundaryCompatibilityTests
{
    [Fact]
    public void DocumentationHelpers_HandleArraysEndingAtInt32MaxValueAcrossPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-array-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "Test-ArrayBoundary.ps1");
            var scalarHelpers = EmbeddedScripts.Load(
                "Scripts/Documentation/Export-HelpJson.DefaultValueScalars.ps1");
            var collectionHelpers = EmbeddedScripts.Load(
                "Scripts/Documentation/Export-HelpJson.DefaultValueCollections.ps1");
            File.WriteAllText(scriptPath, "param([string]$OutputPath)" + Environment.NewLine +
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                scalarHelpers + Environment.NewLine +
                collectionHelpers + Environment.NewLine + """
Add-Type -TypeDefinition @'
using System;
using System.Management.Automation;

public static class PowerForgeArrayBoundaryFixture {
    public static PSDefaultValueAttribute Create() {
        var attribute = new PSDefaultValueAttribute();
        var lowerBound = int.MaxValue - 1;
        Array array;
        try {
            array = Array.CreateInstance(typeof(int), new[] { 2 }, new[] { lowerBound });
        } catch (ArgumentOutOfRangeException) {
            lowerBound--;
            array = Array.CreateInstance(typeof(int), new[] { 2 }, new[] { lowerBound });
        }
        array.SetValue(7, lowerBound);
        array.SetValue(8, lowerBound + 1);
        attribute.Value = array;
        return attribute;
    }
}
'@

function GetCanonicalTypeNameFromType([type]$type) { return $type.FullName }
function GetPowerShellTypeDefaultExpression([type]$type) { return ('[' + $type.FullName + ']') }
function TestPowerShellTypeLiteral([type]$type) { return $true }
function ConvertToPowerShellDefaultValue(
    [object]$value,
    [System.Collections.IList]$referenceStack = $null
) {
    return ([System.IFormattable]$value).ToString(
        $null,
        [System.Globalization.CultureInfo]::InvariantCulture)
}

$attribute = [PowerForgeArrayBoundaryFixture]::Create()
if (TestPSDefaultValueContainsAutomationNull $attribute) {
    throw 'The boundary array unexpectedly contains AutomationNull.'
}
$flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
$valueField = $attribute.GetType().GetField('<Value>k__BackingField', $flags)
$array = [System.Array]$valueField.GetValue($attribute)
$actual = ConvertMultidimensionalArrayToPowerShellDefaultValue `
    -value $array `
    -referenceStack ([System.Collections.ArrayList]::new())
[System.IO.File]::WriteAllText(
    $OutputPath,
    $actual,
    [System.Text.UTF8Encoding]::new($false))
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var outputPath = Path.Combine(root, host.Replace('.', '-') + ".txt");
                var run = new PowerShellRunner().Run(new PowerShellRunRequest(
                    scriptPath,
                    new[] { outputPath },
                    TimeSpan.FromMinutes(1),
                    workingDirectory: root,
                    executableOverride: host));
                Assert.True(run.ExitCode == 0, run.StdErr);
                var lowerBound = host.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)
                    ? 2147483645
                    : 2147483646;
                var expected =
                    $"& {{ $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2), [int[]]@({lowerBound})); $array.SetValue((7), [int[]]@({lowerBound})); $array.SetValue((8), [int[]]@({lowerBound + 1})); return ,$array }}";
                Assert.Equal(expected, File.ReadAllText(outputPath));
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
