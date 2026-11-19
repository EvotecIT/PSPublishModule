using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootRetainsModuleOwnedBlockValues()
    {
        var host = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(host));
        const string source = """
            param([string] $Case = 'RootReturn')
            function New-ReturnedCallback { return { 'ok' } }
            function New-CollectedCallback { $values = @{ one = { 'ok' } }; $values.one }
            function Read-Predicate { param([int] $Value) switch ($Value) { { $_ -eq 1 } { 'ok' } } }
            function Read-Nested { function Invoke-Inner { 'ok' }; Invoke-Inner }
            function Get-Number {
                [CmdletBinding()] param([int] $Value)
                begin { $sum = $Value }
                end { return [int]($sum + 1) }
            }
            switch ($Case) {
                RootReturn { return { 'ok' } }
                RootMap { $values = @{ one = { 'ok' } }; $values.one }
                NamedReturn { New-ReturnedCallback }
                NamedMap { New-CollectedCallback }
                SwitchPredicate { Read-Predicate 1 }
                NestedFunction { Read-Nested }
                Scalar { Get-Number 6 }
            }
            """;
        using var fixture = ArtifactFixture.Create(source);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.RootBlockValues", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var built = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var entries = built.Manifest!.UnitDispositionLedger!.Entries;
        var root = Assert.Single(entries, unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        Assert.False(root.EmittedClrMethod);
        Assert.True(root.RetainedHostedSource);
        foreach (var name in new[] { "New-ReturnedCallback", "New-CollectedCallback", "Read-Predicate" })
            Assert.True(Assert.Single(entries, unit => unit.Name == name).RetainedHostedSource, name);
        Assert.True(Assert.Single(entries, unit => unit.Name == "Read-Nested").EmittedClrMethod);
        var control = Assert.Single(entries, unit => unit.Name == "Get-Number");
        Assert.True(control.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(control));
        foreach (var name in new[] { "RootReturn", "RootMap", "NamedReturn", "NamedMap", "SwitchPredicate", "NestedFunction", "Scalar" })
        {
            var original = RunProcess(host!, "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Case", name);
            var generated = RunProcess(built.ArtifactPath!, "-Case", name);
            Assert.True(original.ExitCode == 0, original.StandardError);
            Assert.Contains(name == "Scalar" ? "7" : "ok", original.StandardOutput, StringComparison.Ordinal);
            Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
                (generated.ExitCode, generated.StandardOutput.Trim(), generated.StandardError.Trim()));
        }
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesOrderedUnboundArguments()
    {
        using var fixture = ArtifactFixture.Create("""
            param([Alias('v')][string]$Value = 'default', [switch]$Flag)
            Write-Output ('value=' + $Value)
            Write-Output ('flag=' + $Flag)
            foreach ($item in $args) { Write-Output ('[' + $item + ']') }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeRootArguments", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.UsesNativeFunctionBinding);

        foreach (var arguments in new[]
                 {
                     new[] { "-Unknown", "extra" },
                     new[] { "-Unknown", "first", "-Unknown", "second" },
                     new[] { "-Value", "owned", "head", "-Unknown", "extra", "tail" },
                     new[] { "-v", "owned", "-Flag:$false", "-Unknown", "a'b;$(throw 'argument executed')" },
                     new[] { "-Unknown;$(throw 'parameter executed')", "literal" },
                     new[] { "-Flag" }
                 })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-File", fixture.ScriptPath }.Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.True(original.ExitCode == 0, string.Join(" | ", arguments) + Environment.NewLine + original.StandardError);
            Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
                (generated.ExitCode, generated.StandardOutput.Trim(), generated.StandardError.Trim()));
        }

        var positionalOnly = RunProcess(result.ArtifactPath!, "-Value", "owned", "--", "-Unknown", "tail");
        Assert.Equal(0, positionalOnly.ExitCode);
        Assert.Equal(new[] { "value=owned", "flag=False", "[-Unknown]", "[tail]" },
            positionalOnly.StandardOutput.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        Assert.Empty(positionalOnly.StandardError);

        var repeatedKnown = new[] { "-Value", "first", "-Value", "second" };
        var originalFailure = RunProcess("pwsh", new[] { "-NoProfile", "-File", fixture.ScriptPath }.Concat(repeatedKnown).ToArray());
        var generatedFailure = RunProcess(result.ArtifactPath!, repeatedKnown);
        Assert.Equal(1, originalFailure.ExitCode);
        Assert.Equal(originalFailure.ExitCode, generatedFailure.ExitCode);
        Assert.Empty(generatedFailure.StandardOutput);
        Assert.Contains("specified more than once", originalFailure.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("specified more than once", generatedFailure.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}
