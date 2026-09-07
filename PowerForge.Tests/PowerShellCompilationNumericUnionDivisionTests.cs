using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("0", false)]
    [InlineData("$Divisor", false)]
    [InlineData("$Items.Length", false)]
    [InlineData("3", true)]
    [InlineData("-3", true)]
    [InlineData("2 + 1", true)]
    public void NumericUnion_RequiresProofThatIntegerDivisorIsNonzero(string divisor, bool supported)
    {
        foreach (var operation in new[] { "/", "%", "/=", "%=" })
        {
            using var fixture = ArtifactFixture.Create(
                "function Get-Result { param([int]$Seed,[int]$Divisor,[int[]]$Items) $Value=$Seed; $Value++; " +
                (operation.EndsWith('=') ? "$Value " + operation + " (" + divisor + "); return $Value" : "return $Value " + operation + " (" + divisor + ")") + " }", ".psm1");
            var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericDivisionMethods", "net10.0");
            Assert.Equal(supported ? 1 : 0, typed.Methods.Length);
        }
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericUnion_PreservesNonzeroIntegerDivisionAndRemainder(string framework, string host)
    {
        var divisors = new[] { -3, -2, -1, 1, 2, 3, int.MaxValue };
        var source = string.Join(Environment.NewLine, divisors.SelectMany((divisor, index) => new[]
        {
            $"function Get-Divide{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; return $Value / ({divisor}) }}",
            $"function Get-Remainder{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; return $Value % ({divisor}) }}",
            $"function Get-UpdatedDivide{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; $Value /= ({divisor}); return $Value }}",
            $"function Get-UpdatedRemainder{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; $Value %= ({divisor}); return $Value }}",
            $"function Get-InitialDivide{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value /= ({divisor}); return $Value }}",
            $"function Get-InitialRemainder{index} {{ param([int]$Seed,[int]$Step) $Value=$Seed; $Value %= ({divisor}); return $Value }}"
        })) + Environment.NewLine + """
            function Get-LoopDivide0 { param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; for([int]$Divisor=3; $Divisor -gt 0; $Divisor--) { $Value=$Value / $Divisor }; return $Value }
            function Get-LoopRemainder0 { param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; for([int]$Divisor=3; $Divisor -gt 0; $Divisor--) { $Value=$Value % $Divisor }; return $Value }
            function Get-LoopUpdatedDivide0 { param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; for([int]$Divisor=3; $Divisor -gt 0; $Divisor--) { $Value /= $Divisor }; return $Value }
            function Get-LoopUpdatedRemainder0 { param([int]$Seed,[int]$Step) $Value=$Seed; $Value+=$Step; for([int]$Divisor=3; $Divisor -gt 0; $Divisor--) { $Value %= $Divisor }; return $Value }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericUnionDivision", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        Assert.Equal(46, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string cases = """
            foreach ($seed in [int]::MinValue,-3,-1,0,1,3,[int]::MaxValue) {
                foreach ($step in 0,1) {
                    foreach ($operation in 'Divide','Remainder','UpdatedDivide','UpdatedRemainder','InitialDivide','InitialRemainder',
                        'LoopDivide','LoopRemainder','LoopUpdatedDivide','LoopUpdatedRemainder') {
                        $indices=if($operation.StartsWith('Loop')) { @(0) } else { @(0..6) }
                        foreach ($index in $indices) {
                            $name=$operation+$index
                            $value=Invoke-Case $name $seed $step
                            [pscustomobject]@{ name=$name; seed=$seed; step=$step; type=$value.GetType().FullName; value=$value } | ConvertTo-Json -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($name,$seed,$step) { & ('Get-'+$name) $seed $step }; " + cases, fixture.RootPath, "original-union-division");
        var compiled = RunStatementErrorProbe(host, "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'); $type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Get_Divide0') }; " +
            "function Invoke-Case($name,$seed,$step) { $type.GetMethod('Get_'+$name).Invoke($null,[object[]]@([int]$seed,[int]$step)) }; " + cases,
            fixture.RootPath, "compiled-union-division");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(644, original.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
