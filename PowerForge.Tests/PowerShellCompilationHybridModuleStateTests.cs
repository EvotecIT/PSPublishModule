using System.Diagnostics;
using System.Management.Automation.Runspaces;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string HybridModuleStateSource = """
        Set-StrictMode -Version Latest
        $script:State = [object[]]@('one', 'two')
        function Get-BridgeState {
            [CmdletBinding()]
            param()
            return $script:State
        }
        function Set-BridgeState {
            [CmdletBinding()]
            param([AllowNull()][object] $Value)
            $script:State = $Value
        }
        function Remove-BridgeState {
            [CmdletBinding()]
            param()
            $command = 'Remove-Variable'
            & $command -Name State -Scope Script -ErrorAction Stop
        }
        Export-ModuleMember -Function Get-BridgeState, Set-BridgeState, Remove-BridgeState
        """;

    [Fact]
    public void Analyze_ModuleStateIsHybridOnlyAndWritesRemainFallback()
    {
        using var fixture = ArtifactFixture.Create(HybridModuleStateSource, ".psm1");
        var analyzer = new PowerShellCompilationAnalyzer();

        Assert.True(PowerShellCompilationBuildSpec.GetCapabilities(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid).HasFlag(PowerShellCompilationCapability.PowerShellModuleState));
        Assert.False(PowerShellCompilationBuildSpec.GetCapabilities(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict).HasFlag(PowerShellCompilationCapability.PowerShellModuleState));

        var hybrid = analyzer.Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));
        var strict = analyzer.Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.True(FindFunction(hybrid, "Get-BridgeState").IsCompilable);
        Assert.False(FindFunction(hybrid, "Set-BridgeState").IsCompilable);
        Assert.False(FindFunction(strict, "Get-BridgeState").IsCompilable);

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HybridModuleStateMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var method = Assert.Single(typed.Methods, static method => method.SourceName == "Get-BridgeState");
        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Set-BridgeState");
        Assert.Equal(new[] { "State" }, method.RequiredPowerShellModuleVariables);
        Assert.Equal(1, method.PowerShellModuleStateReadSiteCount);
        Assert.False(method.RequiresPowerShellRuntimeState);
        Assert.Contains("__readPowerShellModuleVariable(\"State\")", typed.SourceCode, StringComparison.Ordinal);
        var abiMethod = Assert.Single(PowerShellCompilationAbiBuilder.Create(
            typed.NamespaceName,
            typed.TypeName,
            new[] { method }).Methods);
        Assert.DoesNotContain(abiMethod.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellRuntimeState");
        Assert.Contains(abiMethod.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateReader" && parameter.ClrName == "__readPowerShellModuleVariable");
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridModuleReadsTheLiveParentStateWithSourceParity(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(HybridModuleStateSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleState",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        var getter = Assert.Single(ledger.Entries, static entry => entry.Name == "Get-BridgeState");
        Assert.Contains(getter.BoundaryCauses, static cause => cause.Contains("$script:State", StringComparison.Ordinal));
        Assert.True(getter.RuntimeRouted);
        Assert.False(getter.ShapingFallback);
        Assert.Equal(1, getter.BoundaryCrossings);
        Assert.Equal(1, getter.ModuleStateBoundaryCrossings);
        Assert.Contains("HostedModuleState", getter.ArtifactDisposition, StringComparison.Ordinal);
        const string proof =
            "@((Get-BridgeState)).Count; (Get-BridgeState) -join ','; " +
            "Set-BridgeState -Value 'changed'; Get-BridgeState; " +
            "Set-BridgeState -Value ([pscustomobject]@{ Name = 'Ada' }); (Get-BridgeState).Name; " +
            "Remove-BridgeState; try { Get-BridgeState; '<none>' } catch { ($_.FullyQualifiedErrorId -split ',')[0] }";
        var interpreted = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(interpreted, compiled);
        Assert.Equal(new[] { "2", "one,two", "changed", "Ada", "VariableIsUndefined" }, compiled.Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledCmdlets.cs"));
        Assert.Contains("SetModuleVariableReader", generated, StringComparison.Ordinal);
        Assert.Contains("ThrowTerminatingError(result.Error)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridModuleStateIsPerRunspaceAndRemovedWithItsModule()
    {
        using var fixture = ArtifactFixture.Create(HybridModuleStateSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateRunspaces",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var firstState = InitialSessionState.CreateDefault();
        var secondState = InitialSessionState.CreateDefault();
        if (OperatingSystem.IsWindows())
        {
            firstState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
            secondState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        }
        using var first = RunspaceFactory.CreateRunspace(firstState);
        using var second = RunspaceFactory.CreateRunspace(secondState);
        first.Open();
        second.Open();
        var quotedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        Assert.Equal("one", InvokeInRunspace(first, $"Import-Module -Name '{quotedPath}' -Force; Set-BridgeState one; Get-BridgeState"));
        Assert.Equal("two", InvokeInRunspace(second, $"Import-Module -Name '{quotedPath}' -Force; Set-BridgeState two; Get-BridgeState"));
        Assert.Equal("one", InvokeInRunspace(first, "Get-BridgeState"));
        Assert.Equal("cleared", InvokeInRunspace(second,
            "$type = (Get-Command Get-BridgeState).ImplementingType.Assembly.GetTypes() | " +
            "Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
            "$id = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; " +
            "Remove-Module PowerForge.HybridModuleStateRunspaces; " +
            "try { [void]$type.GetMethod('ReadModuleVariable').Invoke($null, @($id, 'State')); 'leaked' } catch { 'cleared' }"));
        Assert.Equal("one", InvokeInRunspace(first, "Get-BridgeState"));
    }

    [Fact]
    public void Build_HybridModuleRollsBackRuntimeHostsWhenInitializationFails()
    {
        const string artifactName = "PowerForge.HybridModuleStateFailure";
        const string source = """
            $script:State = 'initial'
            function Get-FailedState { [CmdletBinding()] param() return $script:State }
            function Get-FailedRegion {
                [CmdletBinding()]
                param()
                return [bool](Microsoft.PowerShell.Management\Test-Path -LiteralPath 'FileSystem::proof' -IsValid -ErrorAction Ignore)
            }
            throw 'module initialization failed'
            Export-ModuleMember -Function Get-FailedState, Get-FailedRegion
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            artifactName,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var escapedAssemblyPath = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, artifactName + ".dll")
            .Replace("'", "''", StringComparison.Ordinal);
        var proof =
            "$assembly = [System.Reflection.Assembly]::LoadFrom('" + escapedAssemblyPath + "'); " +
            "$type = $assembly.GetTypes() | Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
            "$ErrorActionPreference = 'Stop'; try { Import-Module -Name '" + escapedPath + "' -Force } catch { }; " +
            "$id = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; " +
            "$dispatcherPresent = $null -ne $type.GetMethod('GetDispatcher').Invoke($null, @($id)); " +
            "try { [void]$type.GetMethod('ReadModuleVariable').Invoke($null, @($id, 'State')); $reader = 'leaked' } catch { $reader = 'cleared' }; " +
            "\"$dispatcherPresent|$reader\"";
        var run = RunModuleStateHostProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command", proof);

        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal("False|cleared", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        var generated = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("catch {", generated, StringComparison.Ordinal);
        Assert.Contains("::ClearDispatcher", generated, StringComparison.Ordinal);
        Assert.Contains("::ClearModuleVariableReaders", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridModuleInstallsCleanupBeforeAnEarlyModuleReturn()
    {
        const string artifactName = "PowerForge.HybridModuleStateEarlyReturn";
        const string source = """
            $script:State = 'initial'
            function Get-EarlyState { [CmdletBinding()] param() return $script:State }
            return
            Export-ModuleMember -Function Get-EarlyState
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            artifactName,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var proof =
            "$null = Import-Module -Name '" + escapedPath + "' -Force; " +
            "$module = Get-Module -Name '" + artifactName + "' | Where-Object ModuleType -EQ Script | Select-Object -First 1; " +
            "$assembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq '" + artifactName + "' } | Select-Object -First 1; " +
            "$type = $assembly.GetTypes() | Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
            "$id = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; " +
            "$value = $type.GetMethod('ReadModuleVariable').Invoke($null, @($id, 'State')).Value; " +
            "$hasCleanup = $null -ne $module.OnRemove; Remove-Module -ModuleInfo $module -Force; " +
            "try { [void]$type.GetMethod('ReadModuleVariable').Invoke($null, @($id, 'State')); $state = 'leaked' } catch { $state = 'cleared' }; " +
            "\"$hasCleanup|$value|$state\"";
        var run = RunModuleStateHostProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command", proof);

        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal("True|initial|cleared", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleComposesAuthoredOnRemoveWithGeneratedCleanup()
    {
        const string artifactName = "PowerForge.HybridModuleStateAuthoredCleanup";
        const string source = """
            $script:State = 'initial'
            function Get-AuthoredCleanupState { [CmdletBinding()] param() return $script:State }
            function Test-AuthoredCleanupRegion {
                [CmdletBinding()]
                param()
                return [bool](Microsoft.PowerShell.Management\Test-Path -LiteralPath 'FileSystem::proof' -IsValid -ErrorAction Ignore)
            }
            $ExecutionContext.SessionState.Module.OnRemove = {
                $env:PowerForgeHybridAuthoredCleanup = 'ran'
                throw 'authored cleanup failed'
            }
            Export-ModuleMember -Function Get-AuthoredCleanupState, Test-AuthoredCleanupRegion
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            artifactName,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var proof =
            "Remove-Item Env:PowerForgeHybridAuthoredCleanup -ErrorAction Ignore; " +
            "$null = Import-Module -Name '" + escapedPath + "' -Force; " +
            "$module = Get-Module -Name '" + artifactName + "' | Where-Object ModuleType -EQ Script | Select-Object -First 1; " +
            "$type = (Get-Command Get-AuthoredCleanupState).ImplementingType.Assembly.GetTypes() | " +
            "Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
            "$id = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; " +
            "try { Remove-Module -ModuleInfo $module -Force -ErrorAction Stop } catch { }; " +
            "$dispatcherPresent = $null -ne $type.GetMethod('GetDispatcher').Invoke($null, @($id)); " +
            "try { [void]$type.GetMethod('ReadModuleVariable').Invoke($null, @($id, 'State')); $reader = 'leaked' } catch { $reader = 'cleared' }; " +
            "\"$env:PowerForgeHybridAuthoredCleanup|$dispatcherPresent|$reader\"";
        var run = RunModuleStateHostProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command", proof);

        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal("ran|False|cleared", run.StandardOutput.Trim());
    }

    [Fact]
    public void Build_HybridModulePropagatesLiveStateThroughACompiledLocalHelper()
    {
        const string source = """
            $script:State = 'initial'
            function Get-InnerState { [CmdletBinding()] param() return $script:State }
            function Get-OuterState { [CmdletBinding()] param() return Get-InnerState }
            function Set-OuterState { [CmdletBinding()] param([string] $Value) $script:State = $Value }
            Export-ModuleMember -Function Get-OuterState, Set-OuterState
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateHelper",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest!.UnitDispositionLedger);
        var outer = Assert.Single(ledger.Entries, static entry => entry.Name == "Get-OuterState");
        Assert.True(outer.RuntimeRouted);
        Assert.Equal(1, outer.ModuleStateBoundaryCrossings);
        Assert.Contains(outer.BoundaryCauses, static cause => cause.Contains("compiled local call", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "initial", "changed" },
            RunModuleProof(result.ArtifactPath!, "Get-OuterState; Set-OuterState changed; Get-OuterState").Split(Environment.NewLine));
    }

    [Fact]
    public void Generate_RuntimeStateWithoutModuleStateDoesNotGainModuleStateSurface()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HostVersion { [CmdletBinding()] param() return $PSVersionTable.PSVersion }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RuntimeStateOnlyMethods",
            "net10.0");

        var method = Assert.Single(typed.Methods);
        Assert.True(method.RequiresPowerShellRuntimeState);
        Assert.False(method.RequiresPowerShellModuleState);
        Assert.DoesNotContain("__readPowerShellModuleVariable", typed.SourceCode, StringComparison.Ordinal);
        var abiMethod = Assert.Single(PowerShellCompilationAbiBuilder.Create(
            typed.NamespaceName,
            typed.TypeName,
            new[] { method }).Methods);
        Assert.Contains(abiMethod.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellRuntimeState" && parameter.ClrName == "__runtimeState");
        Assert.DoesNotContain(abiMethod.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateReader");

        var generated = PowerShellBinaryCmdletSourceGenerator.Generate(typed, targetFramework: "net10.0");
        Assert.DoesNotContain("ModuleVariableReaders", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadModuleVariable", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsParentScriptModuleState()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StrictState { [CmdletBinding()] param() return $script:State }; Export-ModuleMember -Function Get-StrictState",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictModuleState",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("script:State", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Analyze_UnsafeBracedModuleVariableNameRemainsFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-BracedState { [CmdletBinding()] param() return ${script:State-Name} }",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        var function = FindFunction(plan, "Get-BracedState");
        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("$script:State.Count")]
    [InlineData("$script:State[0]")]
    [InlineData("($script:State).Count")]
    [InlineData("($script:State)[0]")]
    [InlineData("(($script:State)).Count")]
    public void Analyze_ModuleStateMemberAndIndexReceiversRemainFallback(string expression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-StateMember {{ [CmdletBinding()] param() return {expression} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        Assert.False(FindFunction(plan, "Get-StateMember").IsCompilable);
    }

    [Theory]
    [InlineData("$script:State = 2")]
    [InlineData("$script:State += 1")]
    [InlineData("$script:State++")]
    [InlineData("++$script:State")]
    public void Analyze_ModuleStateMutationFormsRemainFallback(string mutation)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Set-State {{ [CmdletBinding()] param() {mutation} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        Assert.False(FindFunction(plan, "Set-State").IsCompilable);
    }

    private static PowerShellCompilationUnitPlan FindFunction(PowerShellCompilationPlan plan, string name)
        => plan.Files.SelectMany(static file => file.Units).Single(unit =>
            unit.Kind == PowerShellCompilationUnitKind.Function &&
            unit.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static (int ExitCode, string StandardOutput, string StandardError) RunModuleStateHostProcess(
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Hybrid module-state host proof did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput, standardError);
    }
}
