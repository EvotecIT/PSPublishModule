using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("(Clear-Local $Trace) -NoEnumerate")]
    [InlineData("-InputObject (Clear-Local $Trace) -NoEnumerate")]
    [InlineData("([object](Clear-Local $Trace)) -NoEnumerate")]
    [InlineData("-InputObject ([object](Clear-Local $Trace)) -NoEnumerate")]
    public void NoEnumerate_CompilesOutputFreeLocalCalls(string arguments)
    {
        using var fixture = ArtifactFixture.Create(
            "function Clear-Local { [CmdletBinding()] param([Collections.ArrayList]$Trace); $Trace.Clear() }; " +
            "function Get-Output { [CmdletBinding()] param([Collections.ArrayList]$Trace); Microsoft.PowerShell.Utility\\Write-Output " + arguments + " }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "OutputFreeLocalCalls", "net10.0");
        Assert.Contains(result.Methods, method => method.SourceName == "Get-Output");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Result=[object](Clear-Local $Trace)")]
    [InlineData("Clear-Local $Trace")]
    [InlineData("[void](Clear-Local $Trace)")]
    public void NoEnumerate_EmptyCallConsumptionCompilesStatementCalls(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Clear-Local { [CmdletBinding()] param([Collections.ArrayList]$Trace); $Trace.Clear() }; " +
            "function Invoke-Caller { [CmdletBinding()] param([Collections.ArrayList]$Trace); " + body + "; 'after' }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "EmptyCallConsumption", "net10.0");
        Assert.Contains(result.Methods, method => method.SourceName == "Invoke-Caller");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("([Console]::WriteLine('probe')) -NoEnumerate")]
    [InlineData("-InputObject ([Console]::WriteLine('probe')) -NoEnumerate")]
    [InlineData("($Trace.Clear()) -NoEnumerate")]
    [InlineData("-InputObject ($Trace.Clear()) -NoEnumerate")]
    [InlineData("([void]$Trace.Add('probe')) -NoEnumerate")]
    [InlineData("-InputObject ([void]$Trace.Add('probe')) -NoEnumerate")]
    public void NoEnumerate_RetainsOutputFreeArgumentExpressions(string arguments)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Output { [CmdletBinding()] param([Collections.ArrayList]$Trace); Microsoft.PowerShell.Utility\\Write-Output " + arguments + " }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "OutputFreeArguments", "net10.0");
        Assert.Empty(result.Methods);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("Write-Output $Value -NoEnumerate")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output $Value -NoEnumerate:$false")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output $Value -NoEnumerate:$Flag")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output $Value $Value -NoEnumerate")]
    public void NoEnumerate_RetainsLookupAndUnqualifiedBindingShapes(string command)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Output { [CmdletBinding()] param([object]$Value,[bool]$Flag); " + command + " }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "OutputBindingBoundaries", "net10.0");
        Assert.True(Assert.Single(result.Methods).RequiresPowerShellCommandRegions);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NoEnumerate_PreservesHostBindingContainersAndIdentity(string framework, string host)
    {
        const string source = """
            function Get-PositionalOutput { [CmdletBinding()] param([object]$Value); Microsoft.PowerShell.Utility\Write-Output $Value -NoEnumerate }
            function Get-NamedOutput { [CmdletBinding()] param([object]$Value); Microsoft.PowerShell.Utility\Write-Output -InputObject $Value -NoEnumerate }
            function Get-SwitchFirstOutput { [CmdletBinding()] param([object]$Value); Microsoft.PowerShell.Utility\Write-Output -NoEnumerate $Value }
            function Get-InlineOutput { [CmdletBinding()] param([object]$Value); Microsoft.PowerShell.Utility\Write-Output -InputObject:$Value -NoEnumerate:$true }
            function Get-CapturedOutput { [CmdletBinding()] param([int]$Count); $Values=for ([int]$i=0;$i -lt $Count;$i++) { $i }; Microsoft.PowerShell.Utility\Write-Output $Values -NoEnumerate; 'after' }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NoEnumerate", PowerShellCompilationArtifactKind.BinaryModule,
            framework == "net472" ? PowerShellCompilationMode.Hybrid : PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(framework == "net472" ? 3 : 5, result.Manifest!.CompiledMethods);
        Assert.Equal(framework == "net472" ? 2 : 0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            function Describe-Value($Value,[int]$Depth=0) {
                if ($null -eq $Value) { return @{type='null'} }
                if ($Depth -gt 4) { return @{type=$Value.GetType().FullName;truncated=$true} }
                if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string] -and $Value -isnot [Collections.IDictionary]) {
                    $items=[Collections.Generic.List[object]]::new()
                    foreach ($item in $Value) { [void]$items.Add((Describe-Value $item ($Depth+1))) }
                    return @{type=$Value.GetType().FullName;items=$items.ToArray()}
                }
                return @{type=$Value.GetType().FullName;value=$Value}
            }
            $cases=@(
                @{name='null';value=$null},
                @{name='int';value=1},
                @{name='byte';value=[byte]1},
                @{name='string';value='abc'},
                @{name='object-empty';value=[object[]]@()},
                @{name='object-one';value=[object[]]@(1)},
                @{name='object-two';value=[object[]]@(1,2)},
                @{name='object-nested';value=[object[]]@(@(1,2),$null)},
                @{name='int-empty';value=[int[]]@()},
                @{name='int-one';value=[int[]]@(1)},
                @{name='int-two';value=[int[]]@(1,2)},
                @{name='list';value=[Collections.ArrayList]@(1,2)},
                @{name='queue';value=[Collections.Queue]@(1,2)},
                @{name='dictionary';value=@{a=1}})
            foreach ($case in $cases) {
                foreach ($style in 'Positional','Named','SwitchFirst','Inline') {
                    $value=$case.value
                    $records=[Collections.Generic.List[object]]::new()
                    & ('Get-'+$style+'Output') -Value $value | ForEach-Object {
                        [void]$records.Add(@{description=(Describe-Value $_);same=[object]::ReferenceEquals($_,$value);
                            json=(ConvertTo-Json -InputObject $_ -Depth 8 -Compress)})
                    }
                    [pscustomobject]@{name=$case.name;style=$style;records=$records.ToArray()} | ConvertTo-Json -Depth 12 -Compress
                }
            }
            foreach ($count in 0,1,3) {
                $records=[Collections.Generic.List[object]]::new()
                Get-CapturedOutput -Count $count | ForEach-Object { [void]$records.Add(@{description=(Describe-Value $_);json=(ConvertTo-Json -InputObject $_ -Depth 8 -Compress)}) }
                [pscustomobject]@{name='capture';count=$count;records=$records.ToArray()} | ConvertTo-Json -Depth 12 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-noenumerate");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-noenumerate");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(59, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
