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
    [InlineData("Write-Host 'x' 6>&1", "win-x64")]
    [InlineData("1..3 | ForEach-Object -Parallel { $_ }", "win-x64")]
    [InlineData("1..3 | ForEach-Object -Begin { 'before' } -Process { $_ }", "win-x64")]
    [InlineData("1..3 | ForEach-Object { return $_ }", "win-x64")]
    [InlineData("1..3 | ForEach-Object { break }", "win-x64")]
    [InlineData("1..3 | ForEach-Object { throw 'stopped' }", "win-x64")]
    [InlineData("1..3 | ForEach-Object { $_ } | Write-Output", "win-x64")]
    [InlineData("1..3 | ForEach-Object -MemberName ToString", "win-x64")]
    [InlineData("function ForEach-Object { param($Action) 'shadow' }; 1..3 | ForEach-Object { $_ }", "win-x64")]
    [InlineData("function script:ForEach-Object { param($Action) 'shadow' }; 1..3 | ForEach-Object { $_ }", "win-x64")]
    [InlineData("if ($true) { function ForEach-Object { param($Action) 'shadow' } }; 1..3 | ForEach-Object { $_ }", "win-x64")]
    [InlineData("if ($true) { function script:ForEach-Object { param($Action) 'shadow' } }; 1..3 | ForEach-Object { $_ }", "win-x64")]
    [InlineData("1..3 | Where-Object -Property IsEven | ForEach-Object { $_ }", "win-x64")]
    [InlineData("1..3 | Where-Object { return $_ } | ForEach-Object { $_ }", "win-x64")]
    [InlineData("1..3 | Where-Object { $_ } | ForEach-Object { break }", "win-x64")]
    [InlineData("1..3 | ForEach-Object { $_ } | Where-Object { $_ }", "win-x64")]
    [InlineData("function script:Where-Object { param($Filter) 'shadow' }; 1..3 | Where-Object { $_ } | ForEach-Object { $_ }", "win-x64")]
    [InlineData("if ($true) { function Where-Object { param($Filter) 'shadow' } }; 1..3 | Where-Object { $_ } | ForEach-Object { $_ }", "win-x64")]
    [InlineData("1..3 | Select-Object -First 1 -OutVariable selected", "win-x64")]
    [InlineData("1..3 | Select-Object -Property @{Name='Value';Expression={ return $_ }}", "win-x64")]
    [InlineData("1..3 | Select-Object -Property @{Name='Value';Expression={ $_.ToString() }}", "win-x64")]
    [InlineData("$outer = 3; 1..3 | Select-Object -Property @{Name='Value';Expression={ $outer }}", "win-x64")]
    [InlineData("1..3 | Select-Object -Property @{Name='Value';Expression={ $script:count++ }}", "win-x64")]
    [InlineData("1..3 | Select-Object @options", "win-x64")]
    [InlineData("function Select-Object { param($First) 'shadow' }; 1..3 | Select-Object -First 1", "win-x64")]
    [InlineData("1..3 | Sort-Object -Culture en-US", "win-x64")]
    [InlineData("1..3 | Group-Object -AsHashTable", "win-x64")]
    [InlineData("1..3 | Select-Object -First 1 | ForEach-Object { $_ }", "win-x64")]
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
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units,
            static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        Assert.False(root.Emitted);
        Assert.True(root.RetainedHostedSource);
    }

    [Theory]
    [InlineData("Write-Output 1; function Get-Later { 2 }; Get-Later")]
    [InlineData("function Get-Blocked { exit 1 }; Get-Blocked")]
    [InlineData("function Foo { 1 }; $function:Foo = { 2 }; Foo")]
    public void Explain_HybridNativeScriptRootRetainsUnsupportedDeclarations(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var root = Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units,
            static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
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

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootRunsUnchangedOfflineQuineWithHostedWriteHost()
    {
        var statementFixtureRoot = System.IO.Path.GetDirectoryName(FindStatementErrorFixtureProject())!;
        var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(statementFixtureRoot,
            "..", "PowerShellCompilationExternalQuine", "Quine.ps1"));
        using var fixture = ArtifactFixture.Create(System.IO.File.ReadAllText(sourcePath));
        Assert.Equal(System.IO.File.ReadAllBytes(sourcePath), System.IO.File.ReadAllBytes(fixture.ScriptPath));
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.Quine", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
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
        Assert.True(root.RuntimeRouted);
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
        Assert.Equal(2, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesInterleavedHostAndSuccessOutput()
    {
        using var fixture = ArtifactFixture.Create("""
            for ($i = 0; $i -lt 3; $i++) {
                Write-Host "host:$i"
                Write-Output "output:$i"
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.InterleavedRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        Assert.Contains("for (", System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.Equal(new[] { "host:0", "output:0", "host:1", "output:1", "host:2", "output:2" },
            generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesWarningAndSuccessOutput()
    {
        using var fixture = ArtifactFixture.Create("""
            for ($i = 0; $i -lt 2; $i++) {
                Write-Warning "warning:$i"
                Write-Output "output:$i"
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.WarningRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        Assert.Contains("for (", System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.Equal(0, generated.ExitCode);
        Assert.Equal(new[] { "WARNING: warning:0", "output:0", "WARNING: warning:1", "output:1" },
            generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesAuthoredWarningShadow()
    {
        using var fixture = ArtifactFixture.Create("""
            function Write-Warning {
                param([string]$Message)
                "shadow:$Message"
            }
            Write-Warning 'careful'
            Write-Output 'done'
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.WarningShadowRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static item => item.Name == "<script>");
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.Equal(new[] { "shadow:careful", "done" },
            generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesVerboseDebugAndInformationOutput()
    {
        using var fixture = ArtifactFixture.Create("""
            for ($i = 0; $i -lt 2; $i++) {
                Write-Verbose "verbose:$i" -Verbose
                Write-Debug "debug:$i" -Debug
                Write-Information "information:$i" -InformationAction Continue
                Write-Output "output:$i"
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.OtherStreamsRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        Assert.Contains("for (", System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.Equal(new[]
        {
            "VERBOSE: verbose:0", "DEBUG: debug:0", "information:0", "output:0",
            "VERBOSE: verbose:1", "DEBUG: debug:1", "information:1", "output:1"
        }, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r')).ToArray());
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesErrorActionContinuationAndStop()
    {
        using var fixture = ArtifactFixture.Create("""
            param([switch]$Stop)
            Write-Output 'before'
            if ($Stop) { Write-Error 'stopped' -ErrorAction Stop }
            else { Write-Error 'continuing' -ErrorAction Continue }
            Write-Output 'after'
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.ErrorRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        foreach (var arguments in new[] { System.Array.Empty<string>(), new[] { "-Stop" } })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal(original.ExitCode, generated.ExitCode);
            Assert.Equal(original.StandardOutput, generated.StandardOutput);
            Assert.Contains(arguments.Length == 0 ? "continuing" : "stopped", original.StandardError);
            Assert.Contains(arguments.Length == 0 ? "continuing" : "stopped", generated.StandardError);
            Assert.Equal(arguments.Length == 0 ? 0 : 1, generated.ExitCode);
            Assert.Equal(arguments.Length == 0 ? new[] { "before", "after" } : new[] { "before" },
                generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesReturnThroughFinally()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$StopAt = 2, [switch]$EmitValue)
            try {
                for ($i = 0; $i -lt 4; $i++) {
                    if ($i -eq $StopAt) {
                        Write-Output "stop:$i"
                        if ($EmitValue) { return "value:$i" }
                        return
                    }
                    Write-Output "step:$i"
                }
            } finally { Write-Output 'finally' }
            Write-Output 'after'
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.ReturnRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var generatedSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("for (", generatedSource);
        Assert.Contains("finally", generatedSource);
        var cases = new (string[] Arguments, string[] ExpectedOutput)[]
        {
            (System.Array.Empty<string>(), new[] { "step:0", "step:1", "stop:2", "finally" }),
            (new[] { "-StopAt", "0" }, new[] { "stop:0", "finally" }),
            (new[] { "-StopAt", "9" }, new[] { "step:0", "step:1", "step:2", "step:3", "finally", "after" }),
            (new[] { "-EmitValue" }, new[] { "step:0", "step:1", "stop:2", "value:2", "finally" })
        };
        foreach (var (arguments, expectedOutput) in cases)
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(0, generated.ExitCode);
            Assert.Equal(expectedOutput, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesTerminalForEachObjectPipelineState()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$End = 3)
            $sum = 0
            if ($End -gt 0) {
                1..$End | ForEach-Object { $sum += $_; $_ * 2 }
            }
            Write-Output "sum:$sum"
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.ForEachRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var generatedSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("AssignTarget", generatedSource);
        Assert.Contains("InvokeCommandRegion", generatedSource);
        foreach (var (arguments, expectedOutput) in new (string[] Arguments, string[] ExpectedOutput)[]
                 {
                     (new[] { "-End", "0" }, new[] { "sum:0" }),
                     (new[] { "-End", "1" }, new[] { "2", "sum:1" }),
                     (System.Array.Empty<string>(), new[] { "2", "4", "6", "sum:6" })
                 })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(0, generated.ExitCode);
            Assert.Equal(expectedOutput, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesWhereForEachPipelineState()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$End = 4)
            $last = 0
            if ($End -gt 0) {
                1..$End | Where-Object { $_ % 2 -eq 0 } | ForEach-Object { $last = $_; "even:$_" }
                1..$End | Where-Object { $_ -gt 2 }
            }
            Write-Output "last:$last"
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.WhereForEachRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var generatedSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("InvokeCommandRegion", generatedSource);
        Assert.Contains("Where-Object", generatedSource);
        foreach (var (arguments, expectedOutput) in new (string[] Arguments, string[] ExpectedOutput)[]
                 {
                     (new[] { "-End", "0" }, new[] { "last:0" }),
                     (new[] { "-End", "1" }, new[] { "last:0" }),
                     (System.Array.Empty<string>(), new[] { "even:2", "even:4", "3", "4", "last:4" })
                 })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(0, generated.ExitCode);
            Assert.Equal(expectedOutput, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesFilteredProjectionAndGrouping()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$Min = 2)
            $rows = @(
                [pscustomobject]@{Name='east';Score=1},
                [pscustomobject]@{Name='west';Score=2},
                [pscustomobject]@{Name='east';Score=3},
                [pscustomobject]@{Name='north';Score=4},
                [pscustomobject]@{Name='west';Score=5}
            )
            $selected = $rows | Where-Object Score -GE $Min | Select-Object -First 2 -Property Name,Score
            foreach ($item in $selected) { Write-Output "selected:$($item.Name):$($item.Score)" }
            $group = $rows | Group-Object -Property Name | Sort-Object -Property Name -Descending |
                Select-Object -First 1 -Property Count,@{Name='Label';Expression={$_.Name}}
            Write-Output "group:$($group.Label):$($group.Count)"
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.PipelineUtilityRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var generatedSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("InvokeCommandRegion", generatedSource);
        Assert.Contains("Select-Object", generatedSource);
        foreach (var (arguments, expectedOutput) in new (string[] Arguments, string[] ExpectedOutput)[]
                 {
                     (new[] { "-Min", "0" }, new[] { "selected:east:1", "selected:west:2", "group:west:2" }),
                     (new[] { "-Min", "4" }, new[] { "selected:north:4", "selected:west:5", "group:west:2" }),
                     (new[] { "-Min", "6" }, new[] { "group:west:2" })
                 })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(0, generated.ExitCode);
            Assert.Equal(expectedOutput, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootStopsEffectfulFilterAtFirstSelectedRecord()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$Count = 8)
            $seen = @()
            $selected = 1..$Count | Where-Object { $seen += $_; $_ % 2 -eq 0 } | Select-Object -First 1
            Write-Output "selected:$selected"
            Write-Output "seen:$($seen -join ',')"
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.StoppingFilterRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(root.Emitted);
        Assert.True(root.RuntimeRouted);
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("InvokeCommandRegion", source);
        foreach (var (arguments, expected) in new (string[] Arguments, string[] Expected)[]
                 {
                     (new[] { "-Count", "1" }, new[] { "selected:", "seen:1" }),
                     (System.Array.Empty<string>(), new[] { "selected:2", "seen:1,2" })
                 })
        {
            var original = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generated = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(0, generated.ExitCode);
            Assert.Equal(expected, generated.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesOwnedFunctionDeclarations()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int]$Count = 4)
            function Invoke {
                param([int]$Value)
                if (($Value % 2) -eq 0) { return $Value * 2 }
                return -1
            }
            function Get-A { 7 }
            function Get_A { 8 }
            Get-A
            Get_A
            for ($i = 0; $i -lt $Count; $i++) { Invoke $i }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.DeclaredRoot", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, spec.TargetContract);
        var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(
            fixture.ScriptPath, plan, "net10.0", spec.SemanticProfileId);
        Assert.Empty(compiled.LocalMethods);
        Assert.Contains("DeclareFunction", compiled.EntryPointMethod.Source);
        Assert.True(Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units,
            static item => item.Kind == PowerShellCompilationUnitKind.Script).Emitted);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static item => item.Name == "<script>");
        Assert.True(root.Emitted);
        Assert.Equal(3, root.RegionGraph!.ScriptBlocks.Count);
        var generated = System.IO.File.ReadAllText(System.IO.Path.Combine(
            result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs"));
        Assert.Contains("DeclareFunction", generated);
        Assert.Contains("for (", generated);
        Assert.Equal(System.IO.File.ReadAllText(fixture.ScriptPath), System.IO.File.ReadAllText(
            System.IO.Path.Combine(result.GeneratedSourcePath!, "Source.ps1")));
        foreach (var arguments in new[] { System.Array.Empty<string>(), new[] { "-Count", "0" },
                     new[] { "-Count", "5" } })
        {
            var originalRun = RunProcess("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                .Concat(arguments).ToArray());
            var generatedRun = RunProcess(result.ArtifactPath!, arguments);
            Assert.Equal((originalRun.ExitCode, originalRun.StandardOutput, originalRun.StandardError),
                (generatedRun.ExitCode, generatedRun.StandardOutput, generatedRun.StandardError));
            Assert.Equal(arguments.Length == 0 ? new[] { "7", "8", "0", "-1", "4", "-1" } :
                    arguments[1] == "0" ? new[] { "7", "8" } : new[] { "7", "8", "0", "-1", "4", "-1", "8" },
                generatedRun.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }
}
