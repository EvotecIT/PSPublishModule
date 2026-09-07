using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Value = [int]2147483647; $Value += 1; return $Value")]
    [InlineData("$Value = [int]2147483647; $Value++; return $Value")]
    [InlineData("$Value = [single]16777216; $Value += [single]1; return $Value")]
    [InlineData("for ($Value = [int]2147483647; $Value -gt 0; $Value++) { }; return $Value")]
    public void Transpile_ValueCastDoesNotConstrainLaterAssignments(string body)
    {
        using var fixture = ArtifactFixture.Create("function Get-CastValue { " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CastValueMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-CastValue");
        Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_StrictValueCastRejectsUnrepresentedPromotion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-CastValue { $Value = [int]2147483647; $Value += 1; return $Value }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.StrictCastAssignment",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.False(result.Succeeded);
        Assert.Contains("promote dynamically", result.Error, StringComparison.Ordinal);
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
