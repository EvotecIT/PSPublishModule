using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string HybridModuleStateWriteSource = """
        Set-StrictMode -Version Latest
        $script:State = 'initial'
        Set-Variable -Scope Script -Name ConstantState -Value 'constant' -Option Constant
        Set-Variable -Scope Script -Name ReadOnlyState -Value 'readonly' -Option ReadOnly
        function Set-WriteState {
            [CmdletBinding()]
            param([object] $Value)
            $script:State = $Value
        }
        function Get-WriteState {
            [CmdletBinding()]
            param()
            return $script:State
        }
        function Set-ThenGetWriteState {
            [CmdletBinding()]
            param([object] $Value)
            $script:State = $Value
            return $script:sTaTe
        }
        function Set-WriteStateCase {
            [CmdletBinding()]
            param()
            $script:sTaTe = 'case'
        }
        function Set-CreatedState {
            [CmdletBinding()]
            param()
            $script:Created = 42
        }
        function Get-CreatedState {
            [CmdletBinding()]
            param()
            return $script:Created
        }
        function Set-ConstantState {
            [CmdletBinding()]
            param([object] $Value)
            $script:ConstantState = $Value
        }
        function Set-ReadOnlyState {
            [CmdletBinding()]
            param([object] $Value)
            $script:ReadOnlyState = $Value
        }
        Export-ModuleMember -Function Set-WriteState, Get-WriteState, Set-ThenGetWriteState, Set-WriteStateCase, Set-CreatedState, Get-CreatedState, Set-ConstantState, Set-ReadOnlyState
        """;

    [Fact]
    public void Transpile_ModuleStateReadAndWriteHaveDistinctBoundedContracts()
    {
        using var fixture = ArtifactFixture.Create(HybridModuleStateWriteSource, ".psm1");
        var analyzer = new PowerShellCompilationAnalyzer();
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

        var hybridSetter = FindFunction(hybrid, "Set-WriteState");
        var hybridGetter = FindFunction(hybrid, "Get-WriteState");
        Assert.True(hybridSetter.IsCompilable, string.Join(Environment.NewLine, hybridSetter.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.True(hybridGetter.IsCompilable, string.Join(Environment.NewLine, hybridGetter.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.False(FindFunction(strict, "Set-WriteState").IsCompilable);
        Assert.False(FindFunction(strict, "Get-WriteState").IsCompilable);

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HybridModuleStateWriteMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var setter = Assert.Single(typed.Methods, static method => method.SourceName == "Set-WriteState");
        var getter = Assert.Single(typed.Methods, static method => method.SourceName == "Get-WriteState");
        var setThenGet = Assert.Single(typed.Methods, static method => method.SourceName == "Set-ThenGetWriteState");

        Assert.True(setter.RequiresPowerShellModuleState);
        Assert.False(setter.RequiresPowerShellModuleStateRead);
        Assert.True(setter.RequiresPowerShellModuleStateWrite);
        Assert.Empty(setter.RequiredPowerShellModuleVariables);
        Assert.Equal(new[] { "State" }, setter.WrittenPowerShellModuleVariables);
        Assert.Equal(0, setter.PowerShellModuleStateReadSiteCount);
        Assert.Equal(1, setter.PowerShellModuleStateWriteSiteCount);
        Assert.Contains("__writePowerShellModuleVariable(\"State\", Value)", typed.SourceCode, StringComparison.Ordinal);

        Assert.True(getter.RequiresPowerShellModuleState);
        Assert.True(getter.RequiresPowerShellModuleStateRead);
        Assert.False(getter.RequiresPowerShellModuleStateWrite);
        Assert.Equal(new[] { "State" }, getter.RequiredPowerShellModuleVariables);
        Assert.Empty(getter.WrittenPowerShellModuleVariables);

        Assert.True(setThenGet.RequiresPowerShellModuleStateRead);
        Assert.True(setThenGet.RequiresPowerShellModuleStateWrite);
        Assert.Equal(new[] { "sTaTe" }, setThenGet.RequiredPowerShellModuleVariables);
        Assert.Equal(new[] { "State" }, setThenGet.WrittenPowerShellModuleVariables);
        Assert.Equal(1, setThenGet.PowerShellModuleStateReadSiteCount);
        Assert.Equal(1, setThenGet.PowerShellModuleStateWriteSiteCount);
        var setThenGetRegions = Assert.IsType<PowerShellCompilationRegionGraph>(setThenGet.RegionGraph).Regions;
        Assert.Equal(2, setThenGetRegions.Count);
        Assert.Contains("ModuleState:STATE", setThenGetRegions[0].Mutations);
        Assert.Contains("ModuleState:STATE", setThenGetRegions[0].Outputs);
        Assert.Contains("ModuleState:STATE", setThenGetRegions[1].Inputs);

        var abi = PowerShellCompilationAbiBuilder.Create(typed.NamespaceName, typed.TypeName, new[] { setter, getter, setThenGet });
        var setterAbi = Assert.Single(abi.Methods, static method => method.PowerShellName == "Set-WriteState");
        var getterAbi = Assert.Single(abi.Methods, static method => method.PowerShellName == "Get-WriteState");
        Assert.Contains(setterAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateWriter" &&
            parameter.ClrName == "__writePowerShellModuleVariable");
        Assert.DoesNotContain(setterAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateReader");
        Assert.Contains(getterAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateReader" &&
            parameter.ClrName == "__readPowerShellModuleVariable");
        Assert.DoesNotContain(getterAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateWriter");
        var setThenGetAbi = Assert.Single(abi.Methods, static method => method.PowerShellName == "Set-ThenGetWriteState");
        Assert.Contains(setThenGetAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateReader");
        Assert.Contains(setThenGetAbi.Parameters, static parameter =>
            parameter.CompilerPurpose == "PowerShellModuleStateWriter");
    }

    [Fact]
    public void Generate_WriteOnlyModuleStateDoesNotGainReaderSurface()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-OnlyState { [CmdletBinding()] param([object] $Value) $script:State = $Value }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "WriteOnlyModuleStateMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var method = Assert.Single(typed.Methods);
        Assert.True(method.RequiresPowerShellModuleStateWrite);
        Assert.False(method.RequiresPowerShellModuleStateRead);
        Assert.DoesNotContain("__readPowerShellModuleVariable", typed.SourceCode, StringComparison.Ordinal);
        var generated = PowerShellBinaryCmdletSourceGenerator.Generate(typed, targetFramework: "net10.0");
        Assert.Contains("ModuleVariableWriters", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleVariableReaders", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadModuleVariable", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("__readPowerShellModuleVariable", "return $script:State")]
    [InlineData("__writePowerShellModuleVariable", "$script:State = 'changed'")]
    public void Transpile_ModuleStateHostParametersRejectAuthoredCollisions(string parameterName, string statement)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Invoke-StateCollision {{ [CmdletBinding()] param([object] ${parameterName}) {statement} }}",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "ModuleStateCollisionMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, diagnostic =>
            diagnostic.FeatureId.Equals("PSL1009", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(parameterName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridModuleWritesLiveParentStateWithSourceParity(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(HybridModuleStateWriteSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateWrites",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.CompiledMethods);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        var setter = Assert.Single(ledger.Entries, static entry => entry.Name == "Set-WriteState");
        Assert.True(setter.RuntimeRouted);
        Assert.False(setter.ShapingFallback);
        Assert.Equal(0, setter.ModuleStateReadBoundaryCrossings);
        Assert.Equal(1, setter.ModuleStateWriteBoundaryCrossings);
        Assert.Equal(1, setter.ModuleStateBoundaryCrossings);
        Assert.Contains(setter.BoundaryCauses, static cause =>
            cause.Contains("writes live parent", StringComparison.Ordinal) &&
            cause.Contains("$script:State", StringComparison.Ordinal));

        const string proof =
            "Set-WriteState -Value 7; $v = @(Get-WriteState); 'scalar:{0}:{1}:{2}' -f $v[0].GetType().FullName, $v.Count, ($v -join ','); " +
            "Set-WriteState -Value $null; if ($null -eq (Get-WriteState)) { 'state:null' } else { 'state:not-null' }; " +
            "Set-WriteState -Value ([object[]]@(1, 2, 3)); $v = @(Get-WriteState); 'array:{0}:{1}' -f $v.Count, ($v -join ','); " +
            "$v = @(Set-ThenGetWriteState -Value ([object[]]@('read', 'write'))); 'readwrite:{0}:{1}' -f $v.Count, ($v -join ','); " +
            "Set-WriteState -Value ([pscustomobject]@{ Name = 'Ada' }); 'object:' + (Get-WriteState).Name; " +
            "Set-WriteStateCase; 'case:' + (Get-WriteState); Set-CreatedState; 'created:' + (Get-CreatedState); " +
            "foreach ($name in 'Set-ConstantState', 'Set-ReadOnlyState') { try { & $name changed; $name + ':unexpected' } catch { '{0}:{1}:{2}' -f $name, ($_.FullyQualifiedErrorId -split ',')[0], $_.Exception.GetType().FullName } }";
        var interpreted = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(interpreted, compiled);
        Assert.Equal(new[]
        {
            "scalar:System.Int32:1:7",
            "state:null",
            "array:3:1,2,3",
            "readwrite:2:read,write",
            "object:Ada",
            "case:case",
            "created:42",
            "Set-ConstantState:VariableNotWritable:System.Management.Automation.SessionStateUnauthorizedAccessException",
            "Set-ReadOnlyState:VariableNotWritable:System.Management.Automation.SessionStateUnauthorizedAccessException"
        }, compiled.Split(Environment.NewLine));

        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledCmdlets.cs"));
        Assert.Contains("ModuleVariableWriters", generated, StringComparison.Ordinal);
        Assert.Contains("WriteModuleVariable", generated, StringComparison.Ordinal);
        Assert.Contains("ThrowPowerShellModuleStateError(result.Error)", generated, StringComparison.Ordinal);
        Assert.Contains("ThrowTerminatingError(moduleStateError)", generated, StringComparison.Ordinal);
        var composed = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("::SetModuleVariableWriter", composed, StringComparison.Ordinal);
        Assert.Contains("$script:State = $value", composed, StringComparison.Ordinal);
        Assert.Contains("::ClearModuleVariableWriters", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridModulePropagatesWriterThroughCompiledLocalCallsAndCleansItUp()
    {
        const string source = """
            $script:State = 'initial'
            function Set-InnerState { [CmdletBinding()] param([string] $Value) $script:State = $Value }
            function Set-OuterState { [CmdletBinding()] param([string] $Value) Set-InnerState -Value $Value }
            function Get-OuterState { [CmdletBinding()] param() return $script:State }
            Export-ModuleMember -Function Set-OuterState, Get-OuterState
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateWriteHelper",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HybridModuleStateWriteHelperMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var typedOuter = Assert.Single(typed.Methods, static method => method.SourceName == "Set-OuterState");
        Assert.True(typedOuter.RequiresPowerShellModuleStateWrite);
        Assert.Empty(typedOuter.WrittenPowerShellModuleVariables);
        var outerGraph = Assert.IsType<PowerShellCompilationRegionGraph>(typedOuter.RegionGraph);
        Assert.Equal(1, outerGraph.ModuleStateWriteBoundarySites);
        Assert.Equal(PowerShellCompilationRegionExecution.Mixed, Assert.Single(outerGraph.Regions).Execution);
        Assert.Contains("LocalCall:SET-INNERSTATE/ModuleStateWrite", Assert.Single(outerGraph.Regions).Mutations);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest!.UnitDispositionLedger);
        var outer = Assert.Single(ledger.Entries, static entry => entry.Name == "Set-OuterState");
        Assert.Equal(1, outer.ModuleStateBoundaryCrossings);
        Assert.Contains(outer.BoundaryCauses, static cause => cause.Contains("compiled local call", StringComparison.Ordinal));

        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var proof =
            "$null = Import-Module -Name '" + escapedPath + "' -Force; Set-OuterState changed; $value = Get-OuterState; " +
            "$module = Get-Module -Name 'PowerForge.HybridModuleStateWriteHelper' | Where-Object ModuleType -EQ Script | Select-Object -First 1; " +
            "$type = (Get-Command Set-OuterState).ImplementingType.Assembly.GetTypes() | Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
            "$id = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; Remove-Module -ModuleInfo $module -Force; " +
            "try { [void]$type.GetMethod('WriteModuleVariable').Invoke($null, @($id, 'State', 'leak')); $writer = 'leaked' } catch { $writer = 'cleared' }; \"$value|$writer\"";
        var run = RunModuleStateHostProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command", proof);

        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal("changed|cleared", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_ModuleStateWriteRhsUsesCanonicalHostRequirementsAndDirectionalEvidence()
    {
        const string source = """
            $script:State = 'initial'
            function Set-StateFromBoundParameter {
                [CmdletBinding()]
                param([string] $Value)
                $script:State = $PSBoundParameters.ContainsKey('Value')
            }
            function Set-StateFromVersion {
                [CmdletBinding()]
                param()
                $script:State = $PSVersionTable.PSVersion
            }
            function Set-StateFromWhatIf {
                [CmdletBinding(SupportsShouldProcess = $true)]
                param()
                $script:State = $WhatIfPreference
            }
            function Set-StateFromHostedCommand {
                [CmdletBinding()]
                param()
                $script:State = [bool](Microsoft.PowerShell.Management\Test-Path -LiteralPath 'FileSystem::PowerForge-Missing-State-Proof' -ErrorAction Ignore)
            }
            function Get-RhsState { [CmdletBinding()] param() return $script:State }
            Export-ModuleMember -Function Set-StateFromBoundParameter, Set-StateFromVersion, Set-StateFromWhatIf, Set-StateFromHostedCommand, Get-RhsState
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateWriteRhs",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true,
            EmitIrSnapshots = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(
            result.Manifest!.CompiledMethods == 5,
            System.Text.Json.JsonSerializer.Serialize(result.Manifest.UnitDispositionLedger));
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HybridModuleStateWriteRhsMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var boundParameter = Assert.Single(typed.Methods, static method => method.SourceName == "Set-StateFromBoundParameter");
        var version = Assert.Single(typed.Methods, static method => method.SourceName == "Set-StateFromVersion");
        var whatIf = Assert.Single(typed.Methods, static method => method.SourceName == "Set-StateFromWhatIf");
        Assert.True(boundParameter.RequiresPowerShellBoundParameters);
        Assert.True(version.RequiresPowerShellRuntimeState);
        Assert.True(whatIf.RequiresPowerShellRuntimeState);
        Assert.Contains("__boundParameters", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("__psVersion", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("__whatIfPreference", typed.SourceCode, StringComparison.Ordinal);

        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        Assert.Equal(4, ledger.SchemaVersion);
        var hosted = Assert.Single(ledger.Entries, static entry => entry.Name == "Set-StateFromHostedCommand");
        Assert.Equal(1, hosted.RuntimeCommandRegions);
        Assert.Equal(0, hosted.ModuleStateReadBoundaryCrossings);
        Assert.Equal(1, hosted.ModuleStateWriteBoundaryCrossings);
        Assert.Equal(1, hosted.ModuleStateBoundaryCrossings);
        Assert.Equal(2, hosted.BoundaryCrossings);
        var hostedGraph = Assert.IsType<PowerShellCompilationRegionGraph>(hosted.RegionGraph);
        var mixedRegion = Assert.Single(hostedGraph.Regions);
        Assert.Equal(PowerShellCompilationRegionExecution.Mixed, mixedRegion.Execution);
        Assert.Equal(1, mixedRegion.HostedCommandBoundarySites);
        Assert.Equal(1, mixedRegion.ModuleStateWriteBoundarySites);
        Assert.Contains("ModuleState:STATE", mixedRegion.Mutations);
        Assert.Equal(3, mixedRegion.StaticBoundaryCostUnits);
        Assert.Equal(1, ledger.ModuleStateReadBoundaryCrossings);
        Assert.Equal(4, ledger.ModuleStateWriteBoundaryCrossings);

        var snapshotPath = Assert.Single(result.Manifest.Files, static file => file.Role == "CompilerIrSnapshot").Path;
        var snapshots = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompilationIrSnapshotBundle>(
            File.ReadAllText(snapshotPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(snapshots);
        Assert.Equal(3, snapshots.SchemaVersion);
        var loweredHosted = Assert.Single(snapshots.Lowered, static unit => unit.Name == "Set-StateFromHostedCommand");
        Assert.Contains("PowerShellModuleStateWrite", loweredHosted.Capabilities);
        Assert.DoesNotContain("PowerShellModuleStateRead", loweredHosted.Capabilities);
        var loweredGetter = Assert.Single(snapshots.Lowered, static unit => unit.Name == "Get-RhsState");
        Assert.Contains("PowerShellModuleStateRead", loweredGetter.Capabilities);
        Assert.DoesNotContain("PowerShellModuleStateWrite", loweredGetter.Capabilities);
        var boundHosted = Assert.Single(snapshots.Bound, static unit => unit.Name == "Set-StateFromHostedCommand");
        Assert.Contains("PowerShellModuleStateWrite", boundHosted.Capabilities);
        Assert.DoesNotContain("PowerShellModuleStateRead", boundHosted.Capabilities);

        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompilationUnitDispositionLedger>(
            System.Text.Json.JsonSerializer.Serialize(ledger));
        Assert.NotNull(roundTrip);
        var roundTripHosted = Assert.Single(roundTrip.Entries, static entry => entry.Name == "Set-StateFromHostedCommand");
        Assert.Equal(1, roundTripHosted.ModuleStateWriteBoundaryCrossings);
        Assert.Equal(0, roundTripHosted.ModuleStateReadBoundaryCrossings);
        Assert.Equal(3, Assert.IsType<PowerShellCompilationRegionGraph>(roundTripHosted.RegionGraph).StaticBoundaryCostUnits);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,
            targetFramework: "net10.0"));
#pragma warning disable CS0618 // The regression verifies that the retained compatibility overload fails closed.
        var legacyError = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationExplanationService.CreateFinal(
                plan,
                PowerShellCompilationArtifactKind.BinaryModule,
                typed));
#pragma warning restore CS0618
        Assert.Contains("directional parent-module state evidence", legacyError.Message, StringComparison.Ordinal);

        const string proof =
            "Set-StateFromBoundParameter -Value present; 'bound:' + (Get-RhsState); " +
            "Set-StateFromVersion; 'version:' + (Get-RhsState).ToString(); " +
            "Set-StateFromWhatIf -WhatIf; 'whatif:' + (Get-RhsState); " +
            "Set-StateFromHostedCommand; 'hosted:' + (Get-RhsState)";
        Assert.Equal(
            RunModuleProof(fixture.ScriptPath, proof, "pwsh"),
            RunModuleProof(result.ArtifactPath!, proof, "pwsh"));
    }

    [Fact]
    public void Build_ModuleStateFailuresRemainCatchableInsideCompiledFunctions()
    {
        const string source = """
            Set-StrictMode -Version Latest
            Set-Variable -Scope Script -Name ConstantState -Value 'constant' -Option Constant
            function Test-CaughtStateWrite {
                [CmdletBinding()]
                param()
                try { $script:ConstantState = 'changed' }
                catch [System.Management.Automation.SessionStateUnauthorizedAccessException] { return 'write-caught' }
                return 'write-missed'
            }
            function Test-CaughtStateRead {
                [CmdletBinding()]
                param()
                try { return $script:MissingState }
                catch [System.Management.Automation.RuntimeException] { return [object] 'read-caught' }
            }
            function Test-CaughtStateWriteContinues {
                [CmdletBinding()]
                param()
                $result = 'before'
                try { $script:ConstantState = 'changed' }
                catch [System.Management.Automation.SessionStateUnauthorizedAccessException] { $result = 'caught' }
                return $result + '-after'
            }
            Export-ModuleMember -Function Test-CaughtStateWrite, Test-CaughtStateRead, Test-CaughtStateWriteContinues
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridModuleStateCaughtErrors",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(
            result.Manifest!.CompiledMethods == 3,
            System.Text.Json.JsonSerializer.Serialize(result.Manifest.UnitDispositionLedger));
        const string proof = "Test-CaughtStateWrite; Test-CaughtStateRead; Test-CaughtStateWriteContinues";
        var interpreted = RunModuleProof(fixture.ScriptPath, proof, "pwsh");
        var compiled = RunModuleProof(result.ArtifactPath!, proof, "pwsh");
        Assert.Equal(interpreted, compiled);
        Assert.Equal(new[] { "write-caught", "read-caught", "caught-after" }, compiled.Split(Environment.NewLine));

        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledCmdlets.cs"));
        Assert.Contains("ExceptionDispatchInfo.Capture(exception).Throw()", generated, StringComparison.Ordinal);
        Assert.Contains("TryTakePowerShellModuleStateError", generated, StringComparison.Ordinal);
        Assert.Contains("ThrowTerminatingError(moduleStateError)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsParentScriptModuleStateWrite()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-StrictState { [CmdletBinding()] param([int] $Value) $script:State = $Value }; Export-ModuleMember -Function Set-StrictState",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictModuleStateWrite",
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
}
