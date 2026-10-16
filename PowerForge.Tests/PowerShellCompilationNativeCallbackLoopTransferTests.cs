using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeScriptCallbackTransfersReachNearestMatchingLoopAndRunFinally()
    {
        using var fixture = ArtifactFixture.Create("""
            $ErrorActionPreference = 'Stop'
            foreach ($iteration in 1..2) {
                try { 1..3 | ForEach-Object { "foreach:$_"; if ($_ -eq 2) { break } } }
                finally { Write-Output 'finally:foreach' }
                Write-Output 'UNREACHED:foreach'
            }
            Write-Output 'after:foreach'
            for ($iteration = 0; $iteration -lt 2; $iteration++) {
                1..3 | ForEach-Object { "for:${iteration}:$_"; if ($_ -eq 2) { continue } }
                Write-Output 'UNREACHED:for'
            }
            Write-Output 'after:for'
            $iteration = 0
            while ($iteration -lt 2) {
                $iteration++
                1..2 | ForEach-Object { "while:$_"; if ($_ -eq 1) { break } }
                Write-Output 'UNREACHED:while'
            }
            Write-Output 'after:while'
            $iteration = 0
            do {
                $iteration++
                1..2 | ForEach-Object { "do:${iteration}:$_"; if ($_ -eq 1) { continue } }
                Write-Output 'UNREACHED:do'
            } while ($iteration -lt 2)
            Write-Output 'after:do'
            $iteration = 0
            do {
                $iteration++
                1..2 | ForEach-Object { "until:$_"; if ($_ -eq 1) { break } }
                Write-Output 'UNREACHED:until'
            } until ($iteration -ge 2)
            Write-Output 'after:until'
            :outer foreach ($outer in 1..2) {
                foreach ($inner in 1..2) {
                    1..2 | ForEach-Object { "label:${outer}:${inner}:$_"; break outer }
                    Write-Output 'UNREACHED:inner'
                }
                Write-Output 'UNREACHED:outer'
            }
            Write-Output 'after:label'
            :again for ($outer = 0; $outer -lt 2; $outer++) {
                foreach ($inner in 1..2) {
                    1..2 | ForEach-Object { "continue:${outer}:${inner}:$_"; continue again }
                    Write-Output 'UNREACHED:continue-inner'
                }
                Write-Output 'UNREACHED:continue-outer'
            }
            Write-Output 'after:continue-label'
            """, compactPath: true);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeCallbackLoopTransfers", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.All(result.Manifest!.UnitDispositionLedger!.Entries, static unit =>
        {
            Assert.True(unit.Emitted);
            Assert.False(unit.RetainedHostedSource);
        });
        var original = RunProcess("pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.True(original.ExitCode == 0, original.StandardError);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.DoesNotContain("UNREACHED", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(new[]
        {
            "foreach:1", "foreach:2", "finally:foreach", "after:foreach",
            "for:0:1", "for:0:2", "for:1:1", "for:1:2", "after:for",
            "while:1", "after:while", "do:1:1", "do:2:1", "after:do",
            "until:1", "after:until", "label:1:1:1", "after:label",
            "continue:0:1:1", "continue:1:1:1", "after:continue-label"
        }, generated.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r')).ToArray());
    }
}
