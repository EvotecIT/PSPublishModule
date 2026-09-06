using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("param([double]$Value) return [int]$Value")]
    [InlineData("param([decimal]$Value) return $Value / 0d")]
    [InlineData("param([int]$Value) return 10 / $Value")]
    [InlineData("param([string]$Value) return [double]$Value")]
    [InlineData("param([double]$Value) return [Math]::Sign($Value)")]
    public void Transpile_CompleteBodyRegionRejectsModeledErrors(string body)
    {
        using var fixture = ArtifactFixture.Create("function retainedComputation { " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
            typed, exportedFunctions: null, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(prepared.Methods);
        Assert.Empty(prepared.PromotedRegions);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridRetainsHeaderAndCompilesImplicitTerminalComputation(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        const string source = """
            function divisorCheck([double]$Number) {
                $limit = [Math]::Sqrt($Number)
                for ($divider = 3; $divider -le $limit; $divider += 2) {
                    if ($Number % $divider -eq 0) { return $false }
                }
                $true
            }
            function multipleOutput([double]$Value) { $Value; $true }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ImplicitComputation",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("function divisorCheck([double]$Number)", generated, StringComparison.Ordinal);
        Assert.Contains("::__PowerForgeRegion_", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("for ($divider", generated, StringComparison.Ordinal);
        const string probe = """
            foreach ($number in @(9.0,25.0,97.0,1009.0,1001.0,[double]::NaN,-1.0)) {
                $values = @(divisorCheck $number)
                '{0}|{1}|{2}' -f $values.Count,$values[0].GetType().FullName,$values[0]
            }
            $multiple = @(multipleOutput 12.5)
            '{0}|{1}|{2}|{3}' -f $multiple.Count,$multiple[0].GetType().FullName,$multiple[0].ToString('R',[cultureinfo]::InvariantCulture),$multiple[1]
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Contains("1|System.Boolean|True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("2|System.Double|12.5|True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_NumericValueProjectionPreservesPromotionAndDoubleConsumers(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        const string source = """
            function Get-Positive {
                [CmdletBinding()] param([double]$Limit)
                $counter = 2147483646
                [int]$iterations = 0
                while ($counter -lt $Limit -and $iterations -lt 4) { $counter += 2; $iterations++ }
                return 0.0 + $counter
            }
            function Get-Negative {
                [CmdletBinding()] param([double]$Offset)
                $counter = 0
                $counter -= 2147483647
                $counter -= 2
                $counter += 2147483647
                return $Offset + $counter
            }
            function Test-Divider {
                [CmdletBinding()] param([double]$Number)
                $limit = [Math]::Sqrt($Number)
                for ($divider = 3; $divider -le $limit; $divider += 2) {
                    if ($Number % $divider -eq 0) { return $false }
                }
                return $true
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NumericProjection",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = """
            $rows = @(
                foreach ($limit in @([double]::NaN, [double]::NegativeInfinity, 2147483645.5, 2147483649.5, [double]::PositiveInfinity)) {
                    $value = Get-Positive $limit
                    '{0}|{1}' -f $value.GetType().FullName, $value.ToString('R',[cultureinfo]::InvariantCulture)
                }
                foreach ($offset in @(0.0,0.25,[double]::NaN,[double]::PositiveInfinity)) {
                    $value = Get-Negative $offset
                    '{0}|{1}' -f $value.GetType().FullName, $value.ToString('R',[cultureinfo]::InvariantCulture)
                }
                foreach ($number in @(9.0,25.0,97.0,1009.0,1001.0,[double]::NaN)) { Test-Divider $number }
            )
            $rows -join ';'
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(15, original.StandardOutput.Trim().Split(';').Length);
        Assert.Contains("2147483654", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("return $counter")]
    [InlineData("return $counter.GetType().Name")]
    [InlineData("return [object]$counter")]
    [InlineData("$copy = $counter; return $copy")]
    [InlineData("return \"counter=$counter\"")]
    [InlineData("return $counter -eq 2")]
    [InlineData("return [Math]::Abs($counter)")]
    [InlineData("[Console]::WriteLine('observe'); return 0.0 + $counter")]
    [InlineData("& { $counter }; return 0.0 + $counter")]
    [InlineData("$counter++; return 0.0 + $counter")]
    [InlineData("$counter *= 2; return 0.0 + $counter")]
    [InlineData("$copy = ($counter += 2); return 0.0 + $copy")]
    public void Transpile_NumericValueProjectionRejectsIdentityAndWiderMutation(string continuation)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Observed { $counter = 2147483646; $counter += 2; " + continuation + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericMethods", "net10.0");
        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
        Assert.Empty(typed.PromotedRegions);
    }
}
