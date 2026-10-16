using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shape_FilteredExecutableRecompilationPreservesPathGuardsAndSourceContext(bool collision)
    {
        var excluded = collision
            ? "$before = Get-Command -Name Read-EarlyValue -ErrorAction Ignore; function Read-EarlyValue { param([int] $Number) return $Number + 1 }"
            : "function NonCmdlet { param([int] $Number) return $Number + 1 }";
        using var fixture = ArtifactFixture.Create("$MyInvocation.MyCommand.Path\n" + excluded +
            "\nfunction Read-File { param([string] $Path) Get-Content -Path $Path -Raw }", compactPath: true);
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.FilteredGuard", "Methods", "net10.0",
            PowerShellCompilationCapabilities.HybridExecutable, guardedExecutableEntryPath: fixture.ScriptPath);
        Assert.Equal(2, typed.Methods.Length);
        Assert.Equal(fixture.ScriptPath, typed.GuardedExecutableEntryPath);
        Assert.Contains(", new int[] { ", typed.SourceCode, StringComparison.Ordinal);

        var filtered = collision
            ? PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
                capabilities: PowerShellCompilationCapabilities.HybridExecutable)
            : PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null,
                targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.HybridExecutable);
        Assert.Equal("Read-File", Assert.Single(filtered.Methods).SourceName);
        Assert.Equal(fixture.ScriptPath, filtered.GuardedExecutableEntryPath);
        Assert.Equal(typed.NativeDeclarationTimingSourcePaths, filtered.NativeDeclarationTimingSourcePaths);
        Assert.Contains(", new int[] { ", filtered.SourceCode, StringComparison.Ordinal);
        Assert.Contains(filtered.Diagnostics, diagnostic => diagnostic.Message.Contains(
            collision ? "command-availability timing" : "Verb-Noun naming", StringComparison.Ordinal));
        var rewritten = PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath);
        var composed = PowerShellHybridModuleComposer.ComposeExecutableRoot(rewritten, fixture.ScriptPath, filtered);
        Assert.DoesNotContain("Get-Content -Path", composed, StringComparison.Ordinal);
        Assert.Contains("InvokeCommandRegion", filtered.SourceCode, StringComparison.Ordinal);

        // The same source/capabilities without selected executable ownership cannot acquire guards.
        var unqualified = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.FilteredGuard", "Unqualified", "net10.0",
            PowerShellCompilationCapabilities.HybridExecutable);
        Assert.Null(unqualified.GuardedExecutableEntryPath);
        Assert.DoesNotContain(", new int[] { ", unqualified.SourceCode, StringComparison.Ordinal);
        var module = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.FilteredGuard", "Module", "net10.0",
            PowerShellCompilationCapabilities.HybridModule, guardedExecutableEntryPath: fixture.ScriptPath);
        Assert.Null(module.GuardedExecutableEntryPath);
        Assert.DoesNotContain(", new int[] { ", module.SourceCode, StringComparison.Ordinal);
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_RetainedExecutableWithFilteredMethodPreservesFileReadAndRejectsVariableProvider()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string] $Path)
            $ErrorActionPreference = 'Stop'
            $MyInvocation.MyCommand.Path | Out-Null
            function NonCmdlet { param([int] $Number) return $Number + 1 }
            function Read-File { param([string] $Path) Get-Content -Path $Path -Raw }
            try { $value = Read-File -Path $Path; "value:$($value.Trim())" }
            catch { "refused:$($_.Exception.Message)" }
            """, compactPath: true);
        var resource = Path.Combine(fixture.RootPath, "data.txt");
        File.WriteAllText(resource, "guarded-file");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Owned.FilteredExecutableGuard", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var built = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var root = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
            static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        Assert.True(root.RetainedHostedSource);
        Assert.False(root.Emitted);
        Assert.Contains(built.Manifest.Diagnostics, static diagnostic => diagnostic.Message.Contains("Verb-Noun naming", StringComparison.Ordinal));
        Assert.Contains(", new int[] { ", File.ReadAllText(Path.Combine(built.GeneratedSourcePath!, "CompiledPowerShell.cs")), StringComparison.Ordinal);

        var positive = RunProcess(built.ArtifactPath!, "-Path", resource);
        Assert.True(positive.ExitCode == 0, $"Exit={positive.ExitCode}; stdout={positive.StandardOutput}; stderr={positive.StandardError}");
        Assert.Equal("value:guarded-file", positive.StandardOutput.Trim());
        Assert.Empty(positive.StandardError);
        var negative = RunProcess(built.ArtifactPath!, "-Path", "Variable:ErrorActionPreference");
        Assert.Equal(0, negative.ExitCode);
        Assert.Contains("refused:", negative.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("must resolve to the active FileSystem provider", negative.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("value:", negative.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(negative.StandardError);
    }
}
