using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_ArrayCollectionCopiesPreserveCardinalityAndIdentity(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-ObjectCopy { [CmdletBinding()] param([object[]]$Items) $Values = @($Items); return ,$Values }
            function Get-IntCopy { [CmdletBinding()] param([int[]]$Items) $Values = @($Items); return ,$Values }
            function Get-StringCopy { [CmdletBinding()] param([string[]]$Items) $Values = @($Items); return ,$Values }
            function Get-DeepCopy { [CmdletBinding()] param([object[]]$Items) $Values = @(@($Items)); return ,$Values }
            function Test-CopyIdentity { [CmdletBinding()] param([object[]]$Items) $First = @($Items); $Second = @($Items); return [object]::ReferenceEquals($First,$Second) }
            function Test-ExplicitLocalIdentity { [int[]]$Items = 1,2; $Values = @($Items); return [object]::ReferenceEquals($Items,$Values) }
            function Test-InferredLocalIdentity { $Items = [int[]](1,2); $Values = @($Items); return [object]::ReferenceEquals($Items,$Values) }
            function Get-StringReference { [CmdletBinding()] param([string[]]$Items) return ,$Items }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ArrayCopies",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type -TypeDefinition 'using System;using System.Collections;public class ForbiddenCopyEnumeration:IEnumerable { public IEnumerator GetEnumerator(){throw new InvalidOperationException("must-not-enumerate-record");} }'
            function Describe($Value) {
                if ($null -eq $Value) { return 'null' }
                if ($Value -is [array]) { return $Value.GetType().Name + '[' + (($Value | ForEach-Object { Describe $_ }) -join ';') + ']' }
                return $Value.GetType().Name + ':' + $Value.ToString()
            }
            $forbidden = [ForbiddenCopyEnumeration]::new()
            $sentinel = [object[]]::new(2)
            $sentinel[0] = [System.Management.Automation.Internal.AutomationNull]::Value
            $sentinel[1] = 1
            foreach ($items in @($null, @(), @(1), @(1,2), @((1,2),(3,4)), @($null,1), (,$forbidden), $sentinel)) {
                foreach ($command in 'Get-ObjectCopy','Get-DeepCopy') {
                    $records = @(& $command -Items $items)
                    $copy = $records[0]
                    '{0}:{1}:{2}:input-alias:{3}' -f $command,$records.Length,(Describe $copy),[object]::ReferenceEquals($items,$copy)
                    if ($null -ne $items -and $items.Length -gt 0) {
                        'element-alias:{0}' -f [object]::ReferenceEquals($items[0],$copy[0])
                    }
                }
            }
            foreach ($command in 'Get-IntCopy','Get-StringCopy') {
                foreach ($items in @($null, @(), @(1), @(1,2))) {
                    Describe (& $command -Items $items)
                }
            }
            $strings = [string[]]::new(2)
            $strings[1] = 'text'
            Describe (Get-StringCopy -Items $strings)
            'string-parameter-alias:{0}' -f [object]::ReferenceEquals($strings,(Get-StringReference -Items $strings))
            'explicit-local-alias:{0}' -f (Test-ExplicitLocalIdentity)
            'inferred-local-alias:{0}' -f (Test-InferredLocalIdentity)
            'empty-identity:{0}' -f (Test-CopyIdentity -Items @())
            'populated-identity:{0}' -f (Test-CopyIdentity -Items @(1))
            $inputValues = @(1,2)
            $changed = Get-ObjectCopy -Items $inputValues
            $changed[0] = 9
            'mutation:{0}:{1}' -f $inputValues[0],$changed[0]
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        if (framework != "net472") Assert.Contains("Get-ObjectCopy:1:Object[][null]:input-alias:False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("empty-identity:True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("string-parameter-alias:True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("explicit-local-alias:" + (framework == "net472" ? "True" : "False"), original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("inferred-local-alias:False", original.StandardOutput, StringComparison.Ordinal);
        if (framework != "net472") Assert.Contains("populated-identity:False", original.StandardOutput, StringComparison.Ordinal);
        if (framework != "net472") Assert.Contains("mutation:1:9", original.StandardOutput, StringComparison.Ordinal);
        Assert.True((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()) ==
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()),
            "ORIGINAL:\n" + original.StandardOutput + "\nGENERATED:\n" + compiled.StandardOutput + "\nERROR:\n" + compiled.StandardError);
    }
}
