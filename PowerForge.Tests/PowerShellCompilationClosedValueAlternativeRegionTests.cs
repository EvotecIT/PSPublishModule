using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeEnvelope_RejectsUnknownAlternativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerForge.Generated.Runtime.PowerShellRegionValueAlternative.Create(-1, "value"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerForge.Generated.Runtime.PowerShellRegionValueAlternative.Create(2, "value"));
    }

    private const string ClosedValueAlternativeModule = """
        function Test-ConditionalValue {
            [CmdletBinding()]
            param(
                [switch]$AsScalar
            )
            if ($AsScalar) {
                [string]$Value = '*'
            } else {
                [string[]]$Value = @('alpha', 'beta')
            }
            data RetainedBarrier { }
            $beforeType = if ($null -eq $Value) { '<null>' } else { $Value.GetType().FullName }
            $beforeValues = @($Value | ForEach-Object { [string]$_ })
            try {
                $Value = 42
                $mutationError = ''
            } catch {
                $mutationError = $_.Exception.GetType().FullName
            }
            [pscustomobject]@{
                scalar = $AsScalar.IsPresent
                beforeType = $beforeType
                beforeCount = $beforeValues.Count
                beforeValues = $beforeValues
                afterType = if ($null -eq $Value) { '<null>' } else { $Value.GetType().FullName }
                afterValues = @($Value | ForEach-Object { [string]$_ })
                mutationError = $mutationError
            }
        }
        Export-ModuleMember -Function Test-ConditionalValue
        """;

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedScalarVectorAlternativeCarriesExactBranchContracts()
    {
        using var fixture = ArtifactFixture.Create(ClosedValueAlternativeModule, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ClosedValueAlternativeMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var matchingRegions = typed.PromotedRegions.Where(static candidate =>
            candidate.ContinuationLocals.Any(static local => local.Alternatives.Count > 0)).ToArray();
        Assert.True(matchingRegions.Length == 1,
            System.Text.Json.JsonSerializer.Serialize(typed.RegionCandidates));
        var region = matchingRegions[0];
        Assert.True(region.RequiresLocalOwnershipGuard);
        Assert.Equal(new[] { "Value" }, region.ContinuationLocals.Select(static local => local.Name));
        var local = Assert.Single(region.ContinuationLocals);
        var contract = Assert.IsType<PowerShellRegionTransferContract>(local.Contract);
        Assert.Equal(PowerShellRegionTransferShape.ClosedValueAlternative, contract.Shape);
        Assert.Equal(PowerShellRegionTransferDirection.LiveOut, contract.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.GuardedFresh, contract.Ownership);
        Assert.Equal(PowerShellRegionTransferMutation.RetainedOnly, contract.Mutation);
        Assert.Equal(new[] { typeof(string).FullName, typeof(string[]).FullName },
            local.Alternatives.Select(static alternative => alternative.TypeName));
        Assert.Equal(new[] { "[string]", "[string[]]" },
            local.Alternatives.Select(static alternative => alternative.TypeConstraintSyntax));
        Assert.Equal(
            new[] { PowerShellRegionTransferShape.StableScalar, PowerShellRegionTransferShape.StableScalarVector },
            local.Alternatives.Select(static alternative => alternative.Contract.Shape));
        Assert.Equal(new[] { "Parameter:ASSCALAR" },
            region.RegionGraph.Regions.Single().Inputs.OrderBy(static input => input, StringComparer.Ordinal));
        Assert.Empty(region.RegionGraph.Regions.Single().Streams);
        Assert.Empty(region.RegionGraph.Regions.Single().Errors);

        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
            System.Text.Json.JsonSerializer.Serialize(region));
        Assert.NotNull(roundTrip);
        var restored = Assert.Single(roundTrip!.ContinuationLocals);
        Assert.Equal(6, Assert.IsType<PowerShellRegionTransferContract>(restored.Contract).SchemaVersion);
        Assert.Equal(2, restored.Alternatives.Count);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ClosedScalarVectorAlternativePreservesTypeShapeAndConstraint(
        string framework,
        string host)
        => AssertClosedScalarVectorAlternative(framework, host, completeFunction: false);

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_NativeScalarVectorSlotsPreserveTypeShapeAndConstraint(string framework, string host)
        => AssertClosedScalarVectorAlternative(framework, host, completeFunction: true);

    private static void AssertClosedScalarVectorAlternative(string framework, string host, bool completeFunction)
    {
        var source = completeFunction ? ClosedValueAlternativeModule.Replace("data RetainedBarrier { }", "", StringComparison.Ordinal) : ClosedValueAlternativeModule;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.ClosedValueAlternative",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        if (completeFunction)
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "Test-ConditionalValue");
            Assert.True(unit.EmittedClrMethod);
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        }
        else Assert.Equal(1, result.Manifest!.PromotedTypedRegions);

        const string probe = """
            $cases = @(
                @{ name = 'scalar'; arguments = @{ AsScalar = $true } },
                @{ name = 'vector'; arguments = @{ AsScalar = $false } },
                @{ name = 'vector-reuse'; arguments = @{ AsScalar = $false } },
                @{ name = 'scalar-reuse'; arguments = @{ AsScalar = $true } }
            )
            foreach ($case in $cases) {
                $arguments = $case.arguments
                $result = Test-ConditionalValue @arguments
                [pscustomobject]@{
                    name = $case.name
                    result = $result
                } | ConvertTo-Json -Depth 8 -Compress
            }
            $stopped = @(Test-ConditionalValue | Select-Object -First 1)
            $reused = Test-ConditionalValue -AsScalar
            [pscustomobject]@{ phase = 'stop-reuse'; stopped = $stopped.Count; reused = $reused } |
                ConvertTo-Json -Depth 8 -Compress
            Remove-Module $module -Force
            $module = Import-Module $modulePath -PassThru -Force
            [pscustomobject]@{ phase = 'reimport'; result = Test-ConditionalValue } |
                ConvertTo-Json -Depth 8 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-value-alternative-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-value-alternative-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedComputerBiosPreservesScalarOrVectorPropertySelection(
        string framework,
        string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Computers", "Get-ComputerBios.ps1");
        Assert.Equal("7ff09bdf93c92b57fd0d18f6bfc009056ebe1eb074d6839da573840e777da3f8",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + Environment.NewLine + """
            function Get-CimData {
                param($ComputerName, $Protocol, $Credential, $Class, $Properties)
                $script:ObservedProperties = [pscustomobject]@{
                    type = if ($null -eq $Properties) { '<null>' } else { $Properties.GetType().FullName }
                    count = @($Properties).Count
                    values = @($Properties | ForEach-Object { [string]$_ })
                    class = $Class
                }
                [pscustomobject]@{
                    PSComputerName = 'computer'
                    Status = 'OK'
                    Version = '1.0'
                    PrimaryBIOS = $true
                    Manufacturer = 'Evotec'
                    ReleaseDate = [datetime]'2026-01-02'
                    SerialNumber = 'serial'
                    SMBIOSBIOSVersion = '1.0'
                    SMBIOSMajorVersion = 1
                    SMBIOSMinorVersion = 2
                    SystemBiosMajorVersion = 3
                    SystemBiosMinorVersion = 4
                }
            }
            function Get-ObservedProperties { $script:ObservedProperties }
            Export-ModuleMember -Function Get-ComputerBios,Get-ObservedProperties
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PinnedComputerBiosAlternative",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var bios = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Get-ComputerBios");
        Assert.True(bios.EmittedClrMethod);
        Assert.True(bios.UsesNativeFunctionBinding);
        Assert.False(bios.RetainedHostedSource);

        const string probe = """
            foreach ($all in $true,$false,$false,$true) {
                $records = if ($all) { @(Get-ComputerBios -All) } else { @(Get-ComputerBios) }
                $observed = Get-ObservedProperties
                [pscustomobject]@{
                    all = $all
                    recordCount = $records.Count
                    recordTypes = @($records | ForEach-Object { $_.GetType().FullName })
                    observed = $observed
                } | ConvertTo-Json -Depth 8 -Compress
            }
            $stopped = @(Get-ComputerBios | Select-Object -First 1)
            $reused = @(Get-ComputerBios -All)
            [pscustomobject]@{ phase = 'stop-reuse'; stopped = $stopped.Count; reused = $reused.Count; observed = Get-ObservedProperties } |
                ConvertTo-Json -Depth 8 -Compress
            Remove-Module $module -Force
            $module = Import-Module $modulePath -PassThru -Force
            $null = @(Get-ComputerBios)
            [pscustomobject]@{ phase = 'reimport'; observed = Get-ObservedProperties } |
                ConvertTo-Json -Depth 8 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "pinned-computer-bios-alternative-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "pinned-computer-bios-alternative-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("System.String[]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("System.String", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedComputerPropertySelectorsPromoteAsOneReusableFamily()
    {
        var cases = new[]
        {
            "Get-ComputerBios.ps1",
            "Get-ComputerCPU.ps1",
            "Get-ComputerDevice.ps1",
            "Get-ComputerDisk.ps1",
            "Get-ComputerDiskLogical.ps1",
            "Get-ComputerOperatingSystem.ps1",
            "Get-ComputerRAM.ps1",
            "Get-ComputerStartup.ps1",
            "Get-ComputerWindowsFeatures.ps1"
        };
        foreach (var file in cases)
        {
            var source = FindCompleteConversionWorkflow(
                "PSSharedGoods", "FullModule", "Public", "Computers", file);
            var name = Path.GetFileNameWithoutExtension(file);
            var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                new[] { source }, "PowerForge.Compiled", name + "Methods", "net10.0",
                PowerShellCompilationCapabilities.HybridModule);
            var matching = typed.PromotedRegions.Where(candidate =>
                candidate.SourceName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                candidate.ContinuationLocals.Any(static local => local.Alternatives.Count > 0)).ToArray();
            Assert.True(matching.Length == 1,
                file + Environment.NewLine + string.Join(Environment.NewLine, typed.RegionCandidates.Select(candidate =>
                    candidate.StartLine + "-" + candidate.EndLine + " " + candidate.DecisionCode + ": " + candidate.Reason +
                    " locals=" + string.Join(",", candidate.ContinuationLocals.Select(local => local.Name + ":" +
                        local.Contract?.Shape)))));
            var region = matching[0];
            var local = Assert.Single(region.ContinuationLocals,
                static candidate => candidate.Alternatives.Count > 0);
            Assert.Equal(PowerShellRegionTransferShape.ClosedValueAlternative,
                Assert.IsType<PowerShellRegionTransferContract>(local.Contract).Shape);
            Assert.Equal(new[] { typeof(string).FullName, typeof(string[]).FullName },
                local.Alternatives.Select(static alternative => alternative.TypeName));
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_TerminalClosedAlternativeRemainsAuthoredInsteadOfLeakingEnvelope()
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-TerminalAlternative {
                param([bool]$Flag)
                if ($Flag) {
                    [string]$Value = 'a'
                } else {
                    [string[]]$Value = @('b', 'c')
                }
            }
            Export-ModuleMember -Function Test-TerminalAlternative
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "TerminalAlternativeMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(typed.Methods);
        Assert.Empty(typed.PromotedRegions);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.TerminalClosedAlternative",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Test-TerminalAlternative");
        Assert.False(unit.EmittedClrMethod);
        Assert.True(unit.RetainedHostedSource);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_SwitchCopyKeepsSwitchParameterRepresentation()
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-SwitchCopy {
                param([switch]$Flag)
                $Copy = $Flag
                data RetainedBarrier { }
                $Copy.GetType().FullName
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "SwitchCopyMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedAlternativeMutationRemainsOutsidePromotedRegion()
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-RetainedMutation {
                param([bool]$Flag)
                if ($Flag) {
                    [string]$Value = 'a'
                } else {
                    [string[]]$Value = @('b', 'c')
                }
                [string]$Value = 'retained'
                data RetainedBarrier { }
                $Value
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RetainedMutationMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var region = Assert.Single(typed.PromotedRegions,
            static candidate => candidate.ContinuationLocals.Any(local =>
                local.Contract?.Shape == PowerShellRegionTransferShape.ClosedValueAlternative));
        Assert.DoesNotContain("retained", region.GeneratedSource, StringComparison.Ordinal);
        Assert.Equal(PowerShellRegionTransferMutation.RetainedOnly,
            Assert.IsType<PowerShellRegionTransferContract>(Assert.Single(region.ContinuationLocals).Contract).Mutation);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedAlternativeRejectsLocalCallCollectionClosure()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Values { [string[]]@('b', 'c') }
            function Test-LocalCallAlternative {
                param([bool]$Flag)
                if ($Flag) {
                    [string]$Value = 'a'
                } else {
                    [string[]]$Value = Get-Values
                }
                data RetainedBarrier { }
                $Value
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "LocalCallAlternativeMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.PromotedRegions,
            static candidate => candidate.ContinuationLocals.Any(local =>
                local.Contract?.Shape == PowerShellRegionTransferShape.ClosedValueAlternative));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("if ($Flag) { [string[]]$Value = @('a') } else { [string]$Value = 'b' }")]
    [InlineData("if ($Flag) { [int]$Value = 1 } else { [int[]]$Value = @() }")]
    public void Transpile_ClosedAlternativeSupportsReversedAndNonStringStableShapes(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Shape { param([bool]$Flag) " + body + "; data RetainedBarrier { }; $Value }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "AlternativeShapeMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(typed.PromotedRegions,
            static candidate => candidate.ContinuationLocals.Any(local =>
                local.Contract?.Shape == PowerShellRegionTransferShape.ClosedValueAlternative));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("if ($Flag) { [object]$Value = 'a' } else { [object[]]$Value = @('b') }")]
    [InlineData("if ($Flag) { [string]$Value = 'a' } else { [int]$Value = 1 }")]
    [InlineData("if ($Flag) { [string[]]$Value = @('a') } else { [int[]]$Value = @(1) }")]
    [InlineData("if ($Flag) { [string]$Value = 'a' } else { [string[,]]$Value = [string[,]]::new(1,1) }")]
    [InlineData("if ($Flag) { [string]$Value = 'a' } else { [int[]]$Value = @(1) }")]
    [InlineData("if ($Flag) { [string]$Value = 'a' }")]
    [InlineData("if ($Flag) { [string]$Value = 'a'; 'output' } else { [string[]]$Value = @('b') }")]
    [InlineData("$Value = 'prior'; if ($Flag) { [string]$Value = 'a' } else { [string[]]$Value = @('b') }")]
    public void Transpile_ClosedAlternativeKeepsOpenOrAmbiguousShapesRetained(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Rejected { param([bool]$Flag) " + body + "; data RetainedBarrier { }; $Value }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RejectedAlternatives", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.PromotedRegions,
            static region => region.ContinuationLocals.Any(static local => local.Alternatives.Count > 0));
    }
}
