using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesBindingStorageAndOutput()
    {
        using var fixture = ArtifactFixture.Create("""
            param([ValidateScript({ $global:ValidationCount++; $_ -ge 0 })][int]$Count = $($global:BindingCount++; 3), [switch]$Fail)
            if ($Fail) { throw [InvalidOperationException]::new('owned root failure') }
            $sum = 0
            for ($i = 0; $i -lt $Count; $i++) { $sum += $i; $i }
            $sum
            $args.Count
            $PSBoundParameters.ContainsKey('Count')
            $global:BindingCount
            $global:ValidationCount
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, spec.TargetContract);
        var explained = Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units);
        Assert.True(explained.Emitted, string.Join("\n", plan.Files.SelectMany(file => file.Units)
            .SelectMany(unit => unit.Diagnostics).Select(diagnostic => diagnostic.Message)));
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.False(root.RetainedHostedSource);
        Assert.True(root.RuntimeRouted);
        Assert.True(result.Manifest.DependencyLockReviewed);
        var generatedSource = System.IO.File.ReadAllText(System.IO.Path.Combine(result.GeneratedSourcePath!, "Source.ps1"));
        Assert.Equal(System.IO.File.ReadAllText(fixture.ScriptPath), generatedSource);
        Assert.Contains("CompiledNativeScriptEntry", System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));
        Assert.Contains("CompiledNativeScriptEntry]::Create", System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "Program.cs")));
        foreach (var arguments in new[] { System.Array.Empty<string>(), new[] { "-Count", "0" },
                     new[] { "-Count", "4", "tail" } })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-File", fixture.ScriptPath }.Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal(0, original.ExitCode);
            Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
                (generated.ExitCode, generated.StandardOutput.Trim(), generated.StandardError.Trim()));
        }
        var originalFailure = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Fail");
        var generatedFailure = RunProcess(result.ArtifactPath!, "-Fail");
        Assert.Equal(1, originalFailure.ExitCode);
        Assert.Equal(originalFailure.ExitCode, generatedFailure.ExitCode);
        Assert.Empty(generatedFailure.StandardOutput);
        Assert.Contains("owned root failure", originalFailure.StandardError);
        Assert.Contains("owned root failure", generatedFailure.StandardError);
        var originalBindingFailure = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Count", "-1");
        var generatedBindingFailure = RunProcess(result.ArtifactPath!, "-Count", "-1");
        Assert.Equal(1, originalBindingFailure.ExitCode);
        Assert.Equal(originalBindingFailure.ExitCode, generatedBindingFailure.ExitCode);
        Assert.Empty(generatedBindingFailure.StandardOutput);
        Assert.Contains("validation", originalBindingFailure.StandardError, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validation", generatedBindingFailure.StandardError, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("$MyInvocation.MyCommand.Name", "win-x64")]
    [InlineData("Get-Variable -Scope 1", "win-x64")]
    [InlineData("param([int]$Count = $(Get-Variable Count -Scope 1)) $Count", "win-x64")]
    [InlineData("Get-ChildItem", "win-x64")]
    [InlineData("& 'Write-Output' 'x'", "win-x64")]
    [InlineData("Write-Output 'x' > output.txt", "win-x64")]
    [InlineData("param([int]$Count = 3) $Count + 1", "linux-x64")]
    public void Explain_HybridNativeScriptRootRetainsUnqualifiedOwners(string source, string runtimeIdentifier)
    {
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", runtimeIdentifier, false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var root = Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units);
        Assert.False(root.Emitted);
        Assert.True(root.RetainedHostedSource);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootRunsUnchangedOfflineBaklava()
    {
        var statementFixtureRoot = System.IO.Path.GetDirectoryName(FindStatementErrorFixtureProject())!;
        var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(statementFixtureRoot,
            "..", "PowerShellCompilationExternalBaklava", "Baklava.ps1"));
        using var fixture = ArtifactFixture.Create(System.IO.File.ReadAllText(sourcePath));
        Assert.Equal(System.IO.File.ReadAllBytes(sourcePath), System.IO.File.ReadAllBytes(fixture.ScriptPath));
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.Baklava", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, spec.TargetContract);
        Assert.True(Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units).Emitted);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.False(root.RetainedHostedSource);
        Assert.True(root.RuntimeRouted); // Authored Write-Output remains a PowerShell command region.
        Assert.Equal(System.IO.File.ReadAllText(sourcePath), System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "Source.ps1")));
        Assert.Contains("CompiledNativeScriptEntry", System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));
        Assert.Contains("CompiledNativeScriptEntry]::Create", System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "Program.cs")));
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", sourcePath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        var lines = generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(21, lines.Length);
    }
}
