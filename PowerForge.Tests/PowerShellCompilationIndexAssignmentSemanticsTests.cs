using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string IndexAssignmentSemanticsSource = """
        function Get-ArrayOrder {
            [CmdletBinding()]
            param()
            [int] $Index = 0
            [int[]] $Values = @(0, 0)
            $Values[$Index] = ($Index = 1)
            return $Values
        }
        function Get-ListOrder {
            [CmdletBinding()]
            param()
            [int] $Index = 0
            [System.Collections.ArrayList] $Values = [System.Collections.ArrayList]::new()
            $null = $Values.Add(0)
            $null = $Values.Add(0)
            $Values[$Index] = ($Index = 1)
            return $Values
        }
        function Get-DictionaryOrder {
            [CmdletBinding()]
            param()
            [string] $Key = 'a'
            [hashtable] $Values = @{ a = 'a'; b = 'old' }
            $Values[$Key] = ($Key = 'b')
            return $Values['b']
        }
        function Get-ArrayFailure {
            [CmdletBinding()]
            param([AllowNull()][int[]] $Values, [int] $Index)
            [int] $Marker = 0
            try {
                $Values[$Index] = ($Marker = 1)
                return -10
            } catch [System.IndexOutOfRangeException] { return $Marker }
            catch { return -1 }
        }
        function Get-NullArrayFailure {
            [CmdletBinding()]
            param([AllowNull()][int[]] $Values, [int] $Index)
            [int] $Marker = 0
            try {
                $Values[$Index] = ($Marker = 1)
                return -10
            } catch [System.Management.Automation.RuntimeException] { return $Marker }
            catch { return -1 }
        }
        function Get-ListFailure {
            [CmdletBinding()]
            param([System.Collections.ArrayList] $Values, [int] $Index)
            [int] $Marker = 0
            try {
                $Values[$Index] = ($Marker = 1)
                return -10
            } catch [System.ArgumentOutOfRangeException] { return $Marker }
            catch { return -1 }
        }
        function Invoke-ArrayOutOfRange {
            [CmdletBinding()]
            param()
            [int[]] $Values = @(0)
            $Values[2] = 1
        }
        function Invoke-ListOutOfRange {
            [CmdletBinding()]
            param()
            [System.Collections.ArrayList] $Values = [System.Collections.ArrayList]::new()
            $null = $Values.Add(0)
            $Values[2] = 1
        }
        Export-ModuleMember -Function Get-ArrayOrder, Get-ListOrder, Get-DictionaryOrder, Get-ArrayFailure, Get-NullArrayFailure, Get-ListFailure, Invoke-ArrayOutOfRange, Invoke-ListOutOfRange
        """;

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_IndexAssignmentPreservesPowerShellOrderAndFailureIdentity(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(IndexAssignmentSemanticsSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.IndexAssignmentSemantics",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.CompiledMethods);
        const string proof =
            "'array-order:' + ((Get-ArrayOrder) -join ','); " +
            "'list-order:' + ((Get-ListOrder) -join ','); Get-DictionaryOrder; " +
            "Get-ArrayFailure -Values @(0) -Index 2; Get-NullArrayFailure -Values $null -Index 0; " +
            "Get-ListFailure -Values ([System.Collections.ArrayList]@(0)) -Index 2; " +
            "try { Invoke-ArrayOutOfRange; 'array:missed' } catch { 'array:' + $_.Exception.GetType().FullName + '|' + (($_.FullyQualifiedErrorId -split ',')[0]) + '|' + $_.CategoryInfo.Category }; " +
            "try { Invoke-ListOutOfRange; 'list:missed' } catch { 'list:' + $_.Exception.GetType().FullName + '|' + (($_.FullyQualifiedErrorId -split ',')[0]) + '|' + $_.CategoryInfo.Category }";
        var interpreted = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(interpreted, compiled);
        Assert.Equal(new[]
        {
            "array-order:0,1",
            "list-order:0,1",
            "b",
            "1",
            "1",
            "1",
            "array:System.IndexOutOfRangeException|System.IndexOutOfRangeException|OperationStopped",
            "list:System.ArgumentOutOfRangeException|System.ArgumentOutOfRangeException|OperationStopped"
        }, compiled.Split(Environment.NewLine));

        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.True(
            generated.IndexOf("__pf_index_value_", StringComparison.Ordinal) <
            generated.IndexOf("__pf_index_target_", StringComparison.Ordinal));
        Assert.Contains("new global::System.IndexOutOfRangeException", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.ArgumentOutOfRangeException", generated, StringComparison.Ordinal);
    }
}
