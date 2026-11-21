namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task NestedPathReadsKeepCallbackRecordsAndScopeOnBothHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Tree)
            if (-not (Test-Path -LiteralPath $Tree -PathType Container)) { 'missing'; exit 2 }
            $lines = 0
            Get-ChildItem -LiteralPath $Tree -File | ForEach-Object {
                $lines += (Get-Content -LiteralPath $_.FullName | Measure-Object -Line).Lines
            }
            $empty = 0
            Get-ChildItem -LiteralPath $Tree -Directory |
                Where-Object { @(Get-ChildItem -LiteralPath $_.FullName).Count -eq 0 } |
                ForEach-Object { $empty++ }
            "lines=$lines|empty=$empty"
            exit 0
            """);
        var tree = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "tree"));
        Directory.CreateDirectory(Path.Combine(tree.FullName, "empty"));
        var occupied = Directory.CreateDirectory(Path.Combine(tree.FullName, "occupied"));
        File.WriteAllLines(Path.Combine(tree.FullName, "record.txt"), new[] { "alpha", "beta" });
        File.WriteAllText(Path.Combine(occupied.FullName, "entry.txt"), "occupied");

        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, Path.Combine(fixture.RootPath, "hybrid"),
            "NestedPathOracle", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var build = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var root = Assert.Single(build.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "<script>");
        Assert.True(root.EmittedClrMethod);
        Assert.False(root.RetainedHostedSource);

        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var (path, output, code) in new[]
                     { (tree.FullName, "lines=2|empty=1", 0), (Path.Combine(fixture.RootPath, "missing"), "missing", 2) })
            {
                var original = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Tree", path }, string.Empty);
                Assert.Equal(code, original.Code);
                Assert.Equal(output, original.Output.Trim());
                Assert.Empty(original.Error);
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath,
                    new[] { "-Tree", path }, string.Empty);
                Assert.Equal(original, compiled);
            }
        }
    }
}
