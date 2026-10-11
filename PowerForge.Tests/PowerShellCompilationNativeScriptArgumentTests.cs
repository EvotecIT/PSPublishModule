using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsFact]
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
