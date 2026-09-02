using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Analyze_BindsPostTestLoopsThroughTypedControlFlow()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-DoWhile { param([int] $Start) [int] $Value = $Start; do { $Value += 1 } while ($Value -lt 3); return $Value }; " +
            "function Invoke-DoUntil { param([int] $Start) [int] $Value = $Start; do { $Value += 1 } until ($Value -ge 3); return $Value }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.Equal(2, Assert.Single(plan.Files).Units.Length);
        Assert.All(
            plan.Files[0].Units,
            unit => Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message))));
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModuleMatchesPowerShellForPostTestLoops(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            "function Invoke-DoWhile { param([int] $Start) [int] $Value = $Start; " +
            "do { $Value += 1; if ($Value -eq 2) { continue }; if ($Value -gt 4) { break } } while ($Value -lt 4); return $Value }; " +
            "function Invoke-DoUntil { param([int] $Start) [int] $Value = $Start; " +
            "do { $Value += 1; if ($Value -eq 2) { continue }; if ($Value -gt 4) { break } } until ($Value -ge 4); return $Value }; " +
            "function Invoke-ConstantPostTest { [int] $Value = 0; do { $Value += 1; continue } while ($false); return $Value }; " +
            "function Invoke-BodyAssigned { do { [string] $Value = 'ok' } while ($Value.Length -lt 2); return $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PostTestLoops",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string invocation =
            "Invoke-DoWhile -Start 0; Invoke-DoWhile -Start 5; " +
            "Invoke-DoUntil -Start 0; Invoke-DoUntil -Start 5; Invoke-ConstantPostTest; Invoke-BodyAssigned";
        var original = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {invocation}");
        var compiled = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {invocation}");

        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Analyze_StillRejectsLabeledPostTestLoopControl()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-LabeledLoop { [int] $Value = 0; :outer do { $Value += 1; continue outer } while ($Value -lt 2); return $Value }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax &&
            string.Equals(diagnostic.FeatureId, "syntax.unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsConditionAssignmentBypassedByContinue()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-ConditionalContinue { param([bool] $Skip) do { if ($Skip) { continue }; [int] $Value = 1 } while ($Value -eq 0); return 42 }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("$Value", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("definitely assigned", StringComparison.Ordinal));
    }
}
