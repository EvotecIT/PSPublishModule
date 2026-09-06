using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$i = 2147483647; return $i + 1")]
    [InlineData("$i++; return $i + 1")]
    [InlineData("$i -= 1; return $i + 1")]
    [InlineData("[int]$i = 2147483647; return $i + 1")]
    [InlineData("$local:i = 2147483647; return $i + 1")]
    [InlineData("& { $i }; return $i + 1")]
    [InlineData("$reference = [ref]$i; return $i + 1")]
    public void Transpile_BoundedCounterRejectsBodyWritesAndObservers(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Unsafe { param([object[]]$Items) for ($i = $Items.Length - 1; $i -gt 0; $i--) { " + body + " }; return 0 }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CounterMethods", "net10.0");
        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridRetainsDynamicCollectionsAndCompilesBoundedCounters(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        const string source = """
            function Get-Collected { [CmdletBinding()] param([object]$Value) [object[]]$items = @($Value); return ,$items }
            function Get-Paired { [CmdletBinding()] param([object]$Value) [object[]]$items = @($Value, $Value); return ,$items }
            function Get-CollectionContinuation { [CmdletBinding()] param([object]$Value) $items = @($Value); return 'after-enumeration' }
            function Get-ReversedIndexes {
                [CmdletBinding()] param([object]$Value)
                $items = [object[]]@($Value)
                for ($i = $items.Length - 1; $i -gt 0; $i--) {
                    $items[$i] = $i + 1
                }
                return ,$items
            }
            function Get-BoundedSum {
                [CmdletBinding()] param([object[]]$Items)
                [int]$Sum = 0
                for ($i = $Items.Length - 1; $i -gt 0; $i--) { $Sum += $i + 1 }
                return $Sum
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.CollectedArrays",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var retained = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("function Get-Collected", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-BoundedSum", retained, StringComparison.Ordinal);
        const string probe = """
            Add-Type -TypeDefinition 'using System;using System.Collections;public class CollectionFailure:IEnumerable { public IEnumerator GetEnumerator(){throw new InvalidOperationException("collection-probe");} }'
            $rows = @(
                foreach ($value in @($null, 'text', 12, @(), @(1), @(1,2,3), @{name='a'}, [Collections.Generic.List[int]]@(4,5))) {
                    $result = @(Get-Collected -Value $value)
                    '{0}|{1}|{2}|{3}' -f $result.Count,$result[0].GetType().FullName,$result[0].Count,($result[0] -join ',')
                }
                $inputValues = @(9,8,7,6)
                $copy = Get-Collected $inputValues
                $copy[0] = 100
                'copy:{0}|{1}' -f $inputValues[0],$copy[0]
                $pair = Get-Paired $inputValues
                'pair:{0}|{1}|{2}' -f $pair.Count,$pair[0].Count,[object]::ReferenceEquals($pair[0],$inputValues)
                foreach ($value in @(@(),@(9),@(9,8,7,6))) {
                    $reversed = @(Get-ReversedIndexes -Value $value)
                    'counter:{0}|{1}|{2}' -f $reversed.Count,$reversed[0].Count,($reversed[0] -join ',')
                }
                foreach ($value in @(@(),@(9),@(9,8,7,6))) {
                    $sum = Get-BoundedSum -Items $value
                    'sum:{0}|{1}' -f $sum.GetType().FullName,$sum
                }
                $Error.Clear()
                $after = Get-CollectionContinuation -Value ([CollectionFailure]::new()) -ErrorAction SilentlyContinue
                'error:{0}|{1}|{2}' -f $after,$Error.Count,$Error[0].FullyQualifiedErrorId
            )
            $rows -join ';'
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Contains("copy:9|100", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("pair:2|4|True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("counter:1|4|9,2,3,4", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("sum:System.Int32|9", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("error:after-enumeration|1|ExceptionInGetEnumerator", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_RuntimeFreeCollectionRejectsUnknownEnumerableIdentity()
    {
        using var fixture = ArtifactFixture.Create("function Get-Collected { param([object]$Value) [object[]]$items = @($Value); return ,$items }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CollectedMethods", "net10.0");
        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
    }
}
