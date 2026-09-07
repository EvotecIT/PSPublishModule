using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_NullableStringRecordsPreserveElementConversion(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-LiteralNull { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) [string[]]$Values = ,$Map['missing']; return ,$Values }
            function Get-CollectedNull { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) [string[]]$Values = @($Map['missing']); return ,$Values }
            function Get-GroupedNull { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) [string[]]$Values = @((,$Map['missing'])); return ,$Values }
            function Get-TypedLiteralNull { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) [string[]]$Values = ,$null; return ,$Values }
            function Get-TypedCollectedNull { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) [string[]]$Values = @($null); return ,$Values }
            function Get-ScalarCast { [CmdletBinding()] param([Collections.Generic.Dictionary[string,string]]$Map) $Text = $Map['missing']; return [string]$Text }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NullableRecords",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(6, result.Manifest!.CompiledMethods);
        const string probe = """
            $map = [Collections.Generic.Dictionary[string,string]]::new()
            for ($case = 0; $case -lt 5; $case++) {
                if ($case -gt 0) { $map['missing'] = @($null, '', 'text', '123')[$case - 1] }
                foreach ($command in 'Get-LiteralNull','Get-CollectedNull','Get-GroupedNull','Get-TypedLiteralNull','Get-TypedCollectedNull') {
                    $values = & $command $map
                    '{0}:{1}:{2}:{3}:{4}' -f $command,$values.Length,($null -eq $values[0]),($values[0] -ceq ''),$values[0]
                }
                $text = Get-ScalarCast $map
                'cast:{0}:{1}:{2}' -f ($null -eq $text),$text.Length,$text
            }
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("Get-GroupedNull:1:False:True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("cast:False:0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_AuthoredArrayCollectionPreservesOneLevelGrouping(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-Parenthesized { $Values = @((1,2)); return ,$Values }
            function Get-Nested { $Values = @((1,2),(3,4)); return ,$Values }
            function Get-WrappedEmpty { $Values = @(,@()); return ,$Values }
            function Get-CollectedEmpty { $Values = @(@()); return ,$Values }
            function Get-Mixed { $Values = @((1,2); $null; ,(3,4)); return ,$Values }
            function Get-Recollected { $Values = @(@((1,2),(3,4))); return ,$Values }
            function Get-Typed { [int[]]$Values = @((1,2); 3); return ,$Values }
            function Get-WrappedNull { $Values = @(,$null); return ,$Values }
            function Get-Wrapped { [CmdletBinding()] param([object]$Value) $Values = @(,$Value); return ,$Values }
            function Get-Pair { [CmdletBinding()] param([object]$Value) $Values = @($Value,$Value); return ,$Values }
            function Test-EmptyCollectedIdentity { $First = @(@()); $Second = @(@()); return [object]::ReferenceEquals($First,$Second) }
            function Test-EmptyDeepIdentity { $First = @(@(@())); $Second = @(@(@())); return [object]::ReferenceEquals($First,$Second) }
            function Test-EmptyObjectIdentity { [object[]]$First = @(@()); [object[]]$Second = @(@()); return [object]::ReferenceEquals($First,$Second) }
            function Test-EmptyIntIdentity { [int[]]$First = @(@()); [int[]]$Second = @(@()); return [object]::ReferenceEquals($First,$Second) }
            function Test-EmptyStringIdentity { [string[]]$First = @(@()); [string[]]$Second = @(@()); return [object]::ReferenceEquals($First,$Second) }
            function Test-EmptyLiteralIdentity { $First = @(); $Second = @(); return [object]::ReferenceEquals($First,$Second) }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ArrayGrouping",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(16, result.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type -TypeDefinition 'using System;using System.Collections;public class ForbiddenEnumeration:IEnumerable { public IEnumerator GetEnumerator(){throw new InvalidOperationException("must-not-enumerate");} }'
            function Describe($Value) {
                if ($null -eq $Value) { return 'null' }
                if ($Value -is [array]) { return $Value.GetType().Name + '[' + (($Value | ForEach-Object { Describe $_ }) -join ';') + ']' }
                return $Value.GetType().Name + ':' + $Value
            }
            foreach ($command in 'Get-Parenthesized','Get-Nested','Get-WrappedEmpty','Get-CollectedEmpty','Get-Mixed','Get-Recollected','Get-Typed','Get-WrappedNull') {
                $records = @(& $command)
                $command + ':' + $records.Count + ':' + (Describe $records[0])
            }
            foreach ($value in @($null, @(), @(1), @(1,2), [System.Management.Automation.Internal.AutomationNull]::Value, [ForbiddenEnumeration]::new())) {
                $wrapped = Get-Wrapped $value
                $pair = Get-Pair $value
                'wrapped:{0}:{1}:pair:{2}:{3}:{4}' -f $wrapped.Length,[object]::ReferenceEquals($wrapped[0],$value),$pair.Length,[object]::ReferenceEquals($pair[0],$value),[object]::ReferenceEquals($pair[1],$value)
            }
            foreach ($command in 'Test-EmptyCollectedIdentity','Test-EmptyDeepIdentity','Test-EmptyObjectIdentity','Test-EmptyIntIdentity','Test-EmptyStringIdentity','Test-EmptyLiteralIdentity') {
                'identity:{0}:{1}' -f $command,(& $command)
            }
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("Get-Nested:1:Object[][Object[][Int32:1;Int32:2];Object[][Int32:3;Int32:4]]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-WrappedEmpty:1:Object[][Object[][]]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-Typed:1:Int32[][Int32:1;Int32:2;Int32:3]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-WrappedNull:1:Object[][null]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("wrapped:1:True:pair:2:True:True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("identity:Test-EmptyCollectedIdentity:" + (framework == "net472" ? "False" : "True"), original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("identity:Test-EmptyIntIdentity:False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("identity:Test-EmptyStringIdentity:False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("identity:Test-EmptyLiteralIdentity:False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("[int[]]$Values = @((1,2),3)")]
    [InlineData("[int[]]$Values = @(@($null))")]
    public void Transpile_CollectedRecordsRetainRequiredElementConversions(string assignment)
    {
        using var fixture = ArtifactFixture.Create("function Get-Converted { " + assignment + "; return ,$Values }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ConvertedMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-Converted");
    }
}
