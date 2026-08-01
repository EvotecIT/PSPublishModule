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
            var runtimeValueHelpers = EmbeddedScripts.Load(
                "Scripts/Documentation/Export-HelpJson.RuntimeValueHelpers.ps1");
            File.WriteAllText(scriptPath, "param([string]$OutputPath)" + Environment.NewLine +
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                runtimeValueHelpers + Environment.NewLine + """
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

$attribute = [PowerForgeArrayBoundaryFixture]::Create()
if (TestPSDefaultValueContainsAutomationNull $attribute) {
    throw 'The boundary array unexpectedly contains AutomationNull.'
}
$flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
$valueField = $attribute.GetType().GetField('<Value>k__BackingField', $flags)
$array = [System.Array]$valueField.GetValue($attribute)
$actual = '{0},{1}' -f $array.GetLowerBound(0),$array.GetUpperBound(0)
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
                var expected = $"{lowerBound},{lowerBound + 1}";
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
