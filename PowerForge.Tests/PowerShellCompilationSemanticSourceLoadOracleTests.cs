namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task GuardedJoinPathLoadsPreserveFileScopeAndExitBehaviorOnBothHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([int] $Units = 0)
            $helperName = 'Private/Load.ps1'
            $helperPath = Join-Path $PSScriptRoot $helperName
            . $helperPath
            . (Join-Path $PSScriptRoot 'Private/Leaf.ps1')
            $scope = Get-LoadedScope
            $result = Get-LeafValue -Units $Units
            "$scope|result=$result"
            if ($Units -lt 0) { exit 2 }
            exit 0
            """);
        var directory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Private"));
        File.WriteAllText(Path.Combine(directory.FullName, "Load.ps1"), "function Get-LoadedScope { return 'scope-ready' }");
        File.WriteAllText(Path.Combine(directory.FullName, "Leaf.ps1"), """
            function Get-LeafValue {
                param([int] $Units)
                if ($Units -lt 0) { return 'invalid' }
                return 'valid'
            }
            """);

        var input = new PowerShellCompilationInputResolver(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .Resolve(fixture.ScriptPath, PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var spec = new PowerShellCompilationBuildSpec(
            input.SourcePath, Path.Combine(fixture.RootPath, "hybrid"), "GuardedJoinPathSource", input.Kind, input.Mode)
        {
            CompilationSourcePaths = input.CompilationSourceFiles,
            RuntimeSourcePaths = input.SourceFiles,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                input.Kind, input.Mode, "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var build = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var root = Assert.Single(build.Manifest!.UnitDispositionLedger!.Entries,
            entry => entry.Name == "<script>" && entry.EmittedClrMethod);
        Assert.False(root.RetainedHostedSource);
        Assert.True(build.Manifest.DependencyLockReviewed);

        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var (units, value, code) in new[] { ("7", "scope-ready|result=valid", 0), ("-1", "scope-ready|result=invalid", 2) })
            {
                var native = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Units", units }, string.Empty);
                Assert.Equal(code, native.Code);
                Assert.Equal(value, native.Output.Trim());
                Assert.Empty(native.Error);
                var hybrid = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath,
                    new[] { "-Units", units }, string.Empty);
                Assert.Equal(native, hybrid);
            }
        }
    }
}
