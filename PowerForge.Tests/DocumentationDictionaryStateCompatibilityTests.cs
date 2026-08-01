using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationDictionaryStateCompatibilityTests
{
    [Fact]
    public void DictionaryHelpers_PreserveOnlyReconstructibleStateAcrossPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-dictionary-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "Test-DictionaryState.ps1");
            var scalarHelpers = EmbeddedScripts.Load(
                "Scripts/Documentation/Export-HelpJson.DefaultValueScalars.ps1");
            var typeHelpers = EmbeddedScripts.Load(
                "Scripts/Documentation/Export-HelpJson.TypeIdentity.ps1");
            File.WriteAllText(scriptPath, "param([string]$OutputPath)" + Environment.NewLine +
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                scalarHelpers + Environment.NewLine +
                typeHelpers + Environment.NewLine + """
function GetCanonicalTypeNameFromType([type]$type) { return $type.FullName }
function GetPowerShellTypeDefaultExpression([type]$type) { return ('[' + $type.FullName + ']') }
function TestPowerShellTypeLiteral([type]$type) { return $true }
function AddDefaultValueReference([object]$value, [System.Collections.IList]$referenceStack) {}

$ordered = [System.Collections.Specialized.OrderedDictionary]::new(100)
$ordered.Add('alpha', 1)
$orderedExpression = GetDictionaryConstructorExpression $ordered ([System.Collections.ArrayList]::new())
$reconstructed = & ([scriptblock]::Create($orderedExpression))
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$initialCapacity = [int]$reconstructed.GetType().GetField('_initialCapacity', $flags).GetValue($reconstructed)
if ($initialCapacity -ne 100) { throw "OrderedDictionary capacity changed to $initialCapacity." }

$dictionary = [System.Collections.Generic.Dictionary[string, int]]::new()
$dictionary.Add('alpha', 1)
$versionField = $null
foreach ($name in @('_version', 'version')) {
    $versionField = $dictionary.GetType().GetField($name, $flags)
    if ($null -ne $versionField) { break }
}
if ($null -eq $versionField) { throw 'Dictionary version field was not found.' }
$versionField.SetValue($dictionary, ([int]$versionField.GetValue($dictionary) + 1))
$rejected = $false
try { [void](GetDictionaryCapacity $dictionary) } catch { $rejected = $true }
if (-not $rejected) { throw 'A non-reconstructible Dictionary serialization version was accepted.' }

[System.IO.File]::WriteAllText(
    $OutputPath,
    $orderedExpression,
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
                Assert.Equal(
                    "[System.Collections.Specialized.OrderedDictionary]::new(([int]100))",
                    File.ReadAllText(outputPath));
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
