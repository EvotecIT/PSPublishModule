using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_MixedDoubleComparisonMatchesPowerShellInBothOperandOrders(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var operators = new[] { "eq", "ne", "lt", "le", "gt", "ge" };
        var source = string.Join(Environment.NewLine, operators.SelectMany(operation => new[]
        {
            "function Test-Int" + operation + " { [CmdletBinding()] param([int]$Whole, [double]$Real); return $Whole -" + operation + " $Real }",
            "function Test-Double" + operation + " { [CmdletBinding()] param([int]$Whole, [double]$Real); return $Real -" + operation + " $Whole }"
        }));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.MixedDoubleComparison",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = """
            $values = @([double]::NegativeInfinity, [double]::NaN, -2147483648.5, -0.5, 0.0, 0.5, 2147483647.5, [double]::PositiveInfinity)
            $rows = foreach ($real in $values) { foreach ($whole in @([int]::MinValue, -1, 0, 1, [int]::MaxValue)) {
                foreach ($op in @('eq','ne','lt','le','gt','ge')) {
                    & ('Test-Int' + $op) -Whole $whole -Real $real
                    & ('Test-Double' + $op) -Whole $whole -Real $real
                }
            } }
            $rows -join '|'
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(480, original.StandardOutput.Trim().Split('|').Length);
        Assert.Contains("True", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("byte", true)]
    [InlineData("sbyte", true)]
    [InlineData("short", true)]
    [InlineData("ushort", true)]
    [InlineData("uint", true)]
    [InlineData("long", false)]
    [InlineData("ulong", false)]
    [InlineData("decimal", false)]
    [InlineData("Nullable[int]", false)]
    public void Transpile_MixedDoubleComparisonRequiresExactNonNullableIntegralWidening(string type, bool supported)
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Mixed { param([" + type + "]$Whole, [double]$Real); return $Whole -lt $Real }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "MixedMethods", "net10.0");
        Assert.Equal(supported ? 1 : 0, typed.Methods.Length);
    }
}
