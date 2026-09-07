using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Value = [int]2147483647; $Value += 1; return $Value", true)]
    [InlineData("$Value = [int]2147483647; $Value++; return $Value", true)]
    [InlineData("$Value = [single]16777216; $Value += [single]1; return $Value", false)]
    [InlineData("for ($Value = [int]2147483647; $Value -gt 0; $Value++) { }; return $Value", true)]
    public void Transpile_ValueCastDoesNotConstrainLaterAssignments(string body, bool represented)
    {
        using var fixture = ArtifactFixture.Create("function Get-CastValue { " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CastValueMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(represented, typed.Methods.Any(method => method.SourceName == "Get-CastValue"));
        if (!represented) Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_StrictValueCastPreservesUnconstrainedPromotion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-CastValue { $Value = [int]2147483647; $Value += 1; return $Value }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.StrictCastAssignment",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(candidate => candidate.Name == "Get_CastValue");
        Assert.Equal(2147483648d, Assert.IsType<double>(method.Invoke(null, null)));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridValueCastPreservesPromotionAndReassignment(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-CastNumber { $Value = [int]2147483647; $Value += 1; return $Value }
            function Get-CastIncrement { $Value = [int]2147483647; $Value++; return $Value }
            function Get-CastSingle { $Value = [single]16777216; $Value += [single]1; return $Value }
            function Get-CastText { $Value = [string]'first'; $Value = 42; return $Value }
            function Get-CastNull { $Value = [string]'first'; $Value = $null; return $null -eq $Value }
            function Get-ConstrainedNull { [string]$Value = 'first'; $Value = $null; return $null -eq $Value }
            function Get-ConstrainedNumber { [int]$Value = 1; $Value += 1; return $Value }
            function Get-StableCast { $Value = [int]42; return $Value }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.CastAssignment",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods >= 3);
        const string probe = "@(Get-CastNumber; Get-CastIncrement; Get-CastSingle; Get-CastText; Get-CastNull; Get-ConstrainedNull; Get-ConstrainedNumber; Get-StableCast) | ForEach-Object { $_.GetType().Name + ':' + $_ }";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }
}
