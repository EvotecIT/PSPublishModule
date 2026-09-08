using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Values = [string[]]$script:State")]
    [InlineData("$Values = $Alias; $Alias = [string[]]$script:State")]
    [InlineData("$Values = [string[]](Read-State)")]
    public void Analyze_LoopCarriedModuleOriginCannotEscapeCollectionBoundary(string mutation)
    {
        using var fixture = ArtifactFixture.Create(
            "function Read-State { return $script:State }; " +
            "function Get-LoopState { param([string[]]$Seed); [string[]]$Values = $Seed; [string[]]$Alias = $Seed; [int]$Index = 0; " +
            "while ($Index -lt 3) { foreach ($Item in $Values) { $null = $Item }; " + mutation + "; $Index += 1 }; return 1 }", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Analyze, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));
        Assert.False(FindFunction(plan, "Get-LoopState").IsCompilable);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Analyze_LoopExitPreservesOriginFromContinuePath()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-LoopExitState {
                param([string[]]$Seed)
                [string[]]$Values = $Seed
                for ([int]$Index = 0; $Index -lt 2; $Index++) {
                    if ($Index -eq 1) { $Values = [string[]]$script:State; continue }
                    $Values = $Seed
                }
                return $Values[0]
            }
            """, ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Analyze, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));
        Assert.False(FindFunction(plan, "Get-LoopExitState").IsCompilable);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_LoopOriginDistinguishesIndependentCollectionsFromOpaqueStringObservers(bool stringify)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-IndependentLoop {
                param([string[]]$Seed)
                for ([int]$Index = 0; $Index -lt 2; $Index++) {
                    $Unused = READ_STATE
                    foreach ($Item in $Seed) { $null = $Item }
                }
                return 1
            }
            """.Replace("READ_STATE", stringify ? "[string]$script:State" : "[object]$script:State", StringComparison.Ordinal), ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Analyze, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));
        var unit = FindFunction(plan, "Get-IndependentLoop");
        Assert.Equal(!stringify, unit.IsCompilable);
        if (stringify) Assert.Contains(unit.Diagnostics, diagnostic => diagnostic.Message.Contains("Opaque string conversion", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("while ($Index -lt 2) { $Text = $Item.ToString(); $Item = $null; $Index += 1 }")]
    [InlineData("for (; $Index -lt 2; $Index++) { $Text = $Item.ToString(); $Item = $null }")]
    [InlineData("do { $Text = $Item.ToString(); $Item = $null; $Index += 1 } while ($Index -lt 2)")]
    [InlineData("do { $Text = $Item.ToString(); $Item = $null; $Index += 1 } until ($Index -ge 2)")]
    [InlineData("foreach ($Item in $Values) { $Text = $Item.ToString() }")]
    [InlineData("$Text = ''; for (; $Index -lt 2; $Text = $Item.ToString()) { $Index += 1; if ($Index -eq 2) { $Item = $null; continue }; $Item = [System.Text.StringBuilder]::new() }")]
    public void Transpile_StrictLoopsDoNotAssumeInitialReceiverRemainsNonNull(string loop)
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-NullableLoop { param([System.Text.StringBuilder[]]$Values); " +
            "$Item = [System.Text.StringBuilder]::new(); [int]$Index = 0; " + loop + "; return 42 }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NullableLoopMethods", "net10.0",
            PowerShellCompilationCapability.None);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Invoke-NullableLoop");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("for (; $Index -lt 2; $Index++) { if ($Index -eq 1) { $Item = $null; continue }; $Item = [System.Text.StringBuilder]::new() }")]
    [InlineData("while ($Index -lt 2) { $Index += 1; if ($Index -eq 2) { $Item = $null; continue }; $Item = [System.Text.StringBuilder]::new() }")]
    [InlineData("do { $Index += 1; if ($Index -eq 2) { $Item = $null; continue }; $Item = [System.Text.StringBuilder]::new() } while ($Index -lt 2)")]
    [InlineData("foreach ($Item in $Values) { }")]
    public void Transpile_StrictLoopExitDoesNotRestoreInitialNonNullFact(string loop)
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-NullableExit { param([System.Text.StringBuilder[]]$Values); " +
            "$Item = [System.Text.StringBuilder]::new(); [int]$Index = 0; " + loop +
            "; $Text = $Item.ToString(); return 42 }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NullableExitMethods", "net10.0",
            PowerShellCompilationCapability.None);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Invoke-NullableExit");
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_StrictLoopCanReestablishNonNullBeforeUse()
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-FreshReceiver {
                param([int]$Limit)
                $Item = [System.Text.StringBuilder]::new()
                for ([int]$Index = 0; $Index -lt $Limit; $Index++) {
                    $Item = [System.Text.StringBuilder]::new()
                    $Text = $Item.ToString()
                    $Item = $null
                }
                return 42
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "FreshReceiverMethods", "net10.0",
            PowerShellCompilationCapability.None);
        Assert.Contains(typed.Methods, method => method.SourceName == "Invoke-FreshReceiver");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridNullableLoopPreservesFailureContinuation(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Invoke-NullableLoop {
                param([System.Text.StringBuilder[]]$Values)
                $Item = [System.Text.StringBuilder]::new()
                [int]$Count = 0
                foreach ($Item in $Values) {
                    $Text = $Item.ToString()
                    $Count += 1
                }
                return $Count
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NullableLoopContinuation",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        const string probe = "$Error.Clear(); $Values = @([System.Text.StringBuilder]::new('a'), $null, [System.Text.StringBuilder]::new('c')); $Output = @(Invoke-NullableLoop -Values $Values 2>$null); [pscustomobject]@{ Values = $Output; Errors = @($Error | ForEach-Object { $_.FullyQualifiedErrorId -replace ',.*$', '' }) } | ConvertTo-Json -Compress";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Contains("\"Values\":[3]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("InvokeMethodOnNull", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }
}
