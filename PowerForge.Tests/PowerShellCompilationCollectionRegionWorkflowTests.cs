using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ClosedCollectionFamiliesPreservePowerShellOutput(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-OrderedBoundary {
                $value = [ordered]@{ first = 1; second = 2 }
                data CollectionBarrier { }
                & { $null = $value.Count }
                $value
            }
            function Get-VectorBoundary {
                [string[]]$value = @('alpha', 'beta')
                data CollectionBarrier { }
                & { $null = $value.Count }
                $value
            }
            function Get-ArrayListBoundary {
                $value = [Collections.ArrayList]::new()
                data CollectionBarrier { }
                & { $null = $value.Count }
                $value
            }
            function Get-ListBoundary {
                $value = [Collections.Generic.List[object]]::new()
                data CollectionBarrier { }
                & { $null = $value.Count }
                $value
            }
            Export-ModuleMember -Function Get-*Boundary
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.CollectionBoundaries",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.PromotedTypedRegions);

        const string probe = """
            foreach ($name in 'Get-OrderedBoundary','Get-VectorBoundary','Get-ArrayListBoundary','Get-ListBoundary') {
                $records = @(& $name)
                [pscustomobject]@{
                    name = $name
                    count = $records.Count
                    types = @($records | ForEach-Object { $_.GetType().FullName })
                    values = @($records | ForEach-Object { [string]$_ })
                } | ConvertTo-Json -Depth 5 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "collection-boundaries-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath,
            "collection-boundaries-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedSplitArrayPreservesAtomicListOutput(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Objects", "Split-Array.ps1");
        Assert.Equal(
            "fb8e80dfad72d1b0d964fbb660dce08c818707ca8e59d4138a0527f1d2ab6e9b",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function Split-Array" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PinnedSplitArray",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.PromotedTypedRegions);

        const string probe = """
            function Describe-Split($name, $arguments) {
                $records = @(Split-Array @arguments)
                $item = $records[0]
                [pscustomobject]@{
                    name = $name
                    recordCount = $records.Count
                    type = if ($null -eq $item) { '<null>' } else { $item.GetType().FullName }
                    itemCount = if ($item -is [System.Collections.IList]) { $item.Count } else { -1 }
                    values = @($records | ForEach-Object { [string]$_ })
                    chunks = if ($item -is [System.Collections.IList]) {
                        @($item | ForEach-Object { @($_) -join ',' })
                    } else { @() }
                } | ConvertTo-Json -Depth 6 -Compress
            }
            Describe-Split 'parts' @{ Objects = [object[]](1..7); Parts = 3 }
            Describe-Split 'size' @{ Objects = [object[]](1..7); Size = 3 }
            Describe-Split 'single' @{ Objects = [object[]]@(42); Parts = 2 }
            Describe-Split 'reuse' @{ Objects = [object[]](8..12); Parts = 2 }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "split-array-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath,
            "split-array-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("System.Collections.Generic.List`1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedSplitArrayPromotesFreshConstructionAndTerminalTransfer()
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Objects", "Split-Array.ps1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { source }, "PowerForge.Compiled", "PinnedSplitArrayMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var regions = typed.PromotedRegions.Where(static region => region.SourceName == "Split-Array")
            .OrderBy(static region => region.StartOffset).ToArray();

        Assert.True(regions.Length == 3,
            string.Join(Environment.NewLine, typed.RegionCandidates.Where(static candidate => candidate.SourceName == "Split-Array")
                .Select(static candidate => candidate.StartLine + "-" + candidate.EndLine + " " + candidate.DecisionCode + ": " + candidate.Reason)) +
            Environment.NewLine + string.Join(Environment.NewLine, typed.RegionOpportunities.Where(static opportunity => opportunity.SourceName == "Split-Array")
                .Select(static opportunity => opportunity.StartLine + "-" + opportunity.EndLine + " " + opportunity.Continuation +
                    " inputs=" + string.Join(",", opportunity.LiveInputs.Select(static input => input.Identity + ":" + input.TypeName)) +
                    " outputs=" + string.Join(",", opportunity.LiveOutputs.Select(static output => output.Identity + ":" + output.TypeName)))));
        Assert.Equal(new[] { 36, 44, 51 }, regions.Select(static region => region.StartLine));
        var envelope = Assert.IsType<PowerShellRegionControlFlowContract>(regions[0].ControlFlowContract);
        Assert.Equal(PowerShellRegionControlFlowBehavior.ReturnOrFallThrough, envelope.Behavior);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.EnumerateOneLevel, envelope.ReturnValue.OutputBehavior);
        Assert.True(regions[1].RequiresLocalOwnershipGuard);
        var fresh = Assert.IsType<PowerShellRegionTransferContract>(Assert.Single(regions[1].ContinuationLocals).Contract);
        Assert.Equal(PowerShellRegionTransferShape.ListSequence, fresh.Shape);
        Assert.Equal(PowerShellRegionTransferDirection.LiveOut, fresh.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.GuardedFresh, fresh.Ownership);
        Assert.Empty(regions[2].ContinuationLocals);
        var input = Assert.Single(regions[2].InputLocals);
        Assert.Equal("outArray", input.Name, ignoreCase: true);
        var earlier = Assert.IsType<PowerShellRegionTransferContract>(input.Contract);
        Assert.Equal(PowerShellRegionTransferDirection.LiveIn, earlier.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.EarlierRegion, earlier.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.NoEnumerate,
            Assert.IsType<PowerShellRegionTransferContract>(regions[2].TerminalTransferContract).OutputBehavior);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ConditionalReturnsPreserveClosedCollectionOutputContracts(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-VectorReturn { param([string[]]$Value, [bool]$Return) if ($Return) { return $Value }; 'continued' }
            function Get-ArrayReturn { param([Array]$Value, [bool]$Return) if ($Return) { return $Value }; 'continued' }
            function Get-ArrayListReturn { param([Collections.ArrayList]$Value, [bool]$Return) if ($Return) { return $Value }; 'continued' }
            function Get-ListReturn { param([Collections.Generic.List[object]]$Value, [bool]$Return) if ($Return) { return $Value }; 'continued' }
            function Get-MapReturn { param([hashtable]$Value, [bool]$Return) if ($Return) { return $Value }; 'continued' }
            function Get-NoEnumerateReturn { param([Array]$Value, [bool]$Return) if ($Return) { return ,$Value }; 'continued' }
            Export-ModuleMember -Function Get-*Return
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ConditionalCollectionReturns", framework,
            PowerShellCompilationCapabilities.HybridModule);
        var controls = typed.PromotedRegions.Where(static region => region.ControlFlowContract is not null)
            .OrderBy(static region => region.SourceName, StringComparer.Ordinal).ToArray();
        Assert.Equal(6, controls.Length);
        AssertControlReturn(controls, "Get-VectorReturn", PowerShellRegionTransferShape.StableScalarVector,
            PowerShellRegionTransferOutputBehavior.EnumerateOneLevel);
        AssertControlReturn(controls, "Get-ArrayReturn", PowerShellRegionTransferShape.ListSequence,
            PowerShellRegionTransferOutputBehavior.EnumerateOneLevel);
        AssertControlReturn(controls, "Get-ArrayListReturn", PowerShellRegionTransferShape.ListSequence,
            PowerShellRegionTransferOutputBehavior.EnumerateOneLevel);
        AssertControlReturn(controls, "Get-ListReturn", PowerShellRegionTransferShape.ListSequence,
            PowerShellRegionTransferOutputBehavior.EnumerateOneLevel);
        AssertControlReturn(controls, "Get-MapReturn", PowerShellRegionTransferShape.AtomicMap,
            PowerShellRegionTransferOutputBehavior.Atomic);
        AssertControlReturn(controls, "Get-NoEnumerateReturn", PowerShellRegionTransferShape.ListSequence,
            PowerShellRegionTransferOutputBehavior.NoEnumerate);
        Assert.All(controls, static region =>
        {
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
                System.Text.Json.JsonSerializer.Serialize(region));
            Assert.NotNull(roundTrip?.ControlFlowContract);
            Assert.Equal(region.ControlFlowContract!.ReturnValue.OutputBehavior,
                roundTrip!.ControlFlowContract!.ReturnValue.OutputBehavior);
        });

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.ConditionalCollectionReturns",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        const string probe = """
            $arrayList = [Collections.ArrayList]::new()
            [void]$arrayList.Add('alpha'); [void]$arrayList.Add('beta')
            $emptyArrayList = [Collections.ArrayList]::new()
            $list = [Collections.Generic.List[object]]::new()
            $list.Add('alpha'); $list.Add('beta')
            $emptyList = [Collections.Generic.List[object]]::new()
            $nestedArray = [object[]]::new(2)
            $nestedArray[0] = [object[]]@('inner-a','inner-b')
            $nestedArray[1] = 'tail'
            $cases = @(
                @{ Name = 'Get-VectorReturn'; Value = [string[]]@() },
                @{ Name = 'Get-VectorReturn'; Value = [string[]]@('alpha') },
                @{ Name = 'Get-VectorReturn'; Value = [string[]]@('alpha','beta') },
                @{ Name = 'Get-ArrayReturn'; Value = $null },
                @{ Name = 'Get-ArrayReturn'; Value = [object[]]@() },
                @{ Name = 'Get-ArrayReturn'; Value = [object[]]@('alpha') },
                @{ Name = 'Get-ArrayReturn'; Value = $nestedArray },
                @{ Name = 'Get-ArrayReturn'; Value = [object[]]@('alpha','beta') },
                @{ Name = 'Get-ArrayListReturn'; Value = $emptyArrayList },
                @{ Name = 'Get-ArrayListReturn'; Value = $arrayList },
                @{ Name = 'Get-ListReturn'; Value = $emptyList },
                @{ Name = 'Get-ListReturn'; Value = $list },
                @{ Name = 'Get-MapReturn'; Value = @{} },
                @{ Name = 'Get-MapReturn'; Value = @{ first = 1; second = 2 } },
                @{ Name = 'Get-NoEnumerateReturn'; Value = [object[]]@() },
                @{ Name = 'Get-NoEnumerateReturn'; Value = [object[]]@('alpha','beta') }
            )
            foreach ($case in $cases) {
                foreach ($return in $true,$false) {
                    $records = @(& $case.Name -Value $case.Value -Return:$return)
                    [pscustomobject]@{
                        name = $case.Name
                        return = $return
                        count = $records.Count
                        types = @($records | ForEach-Object { if ($null -eq $_) { '<null>' } else { $_.GetType().FullName } })
                        values = @($records | ForEach-Object { [string]$_ })
                    } | ConvertTo-Json -Depth 5 -Compress
                }
                $stopped = @(& $case.Name -Value $case.Value -Return:$true | Select-Object -First 1)
                $reused = @(& $case.Name -Value $case.Value -Return:$true)
                [pscustomobject]@{
                    name = $case.Name
                    phase = 'stop-and-reuse'
                    stopped = @($stopped | ForEach-Object { [string]$_ })
                    reused = @($reused | ForEach-Object { [string]$_ })
                    inputCount = if ($case.Value -is [Collections.ICollection]) { $case.Value.Count } else { -1 }
                } | ConvertTo-Json -Depth 5 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "conditional-collection-return-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath,
            "conditional-collection-return-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    private static void AssertControlReturn(
        PowerShellCompiledRegion[] regions,
        string sourceName,
        PowerShellRegionTransferShape shape,
        PowerShellRegionTransferOutputBehavior outputBehavior)
    {
        var control = Assert.IsType<PowerShellRegionControlFlowContract>(
            Assert.Single(regions, region => region.SourceName == sourceName).ControlFlowContract);
        Assert.Equal(PowerShellRegionControlFlowBehavior.ReturnOrFallThrough, control.Behavior);
        Assert.Equal(shape, control.ReturnValue.Shape);
        Assert.Equal(PowerShellRegionTransferDirection.TerminalSuccess, control.ReturnValue.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.ParameterBorrowed, control.ReturnValue.Ownership);
        Assert.Equal(outputBehavior, control.ReturnValue.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferMutation.None, control.ReturnValue.Mutation);
        if (outputBehavior == PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)
        {
            Assert.Equal(PowerShellRegionEnumerationOwner.RetainedPowerShell, control.ReturnValue.EnumerationOwner);
            Assert.Equal(PowerShellRegionEnumerationFailureBehavior.PreservePartialSuccessAndStatementContinuation,
                control.ReturnValue.EnumerationFailureBehavior);
            Assert.Equal(PowerShellRegionEnumeratorLifetime.RetainedPowerShell, control.ReturnValue.EnumeratorLifetime);
        }
        else
        {
            Assert.Equal(PowerShellRegionEnumerationOwner.None, control.ReturnValue.EnumerationOwner);
            Assert.Equal(PowerShellRegionEnumerationFailureBehavior.None, control.ReturnValue.EnumerationFailureBehavior);
            Assert.Equal(PowerShellRegionEnumeratorLifetime.None, control.ReturnValue.EnumeratorLifetime);
        }
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("param([object]$Value,[bool]$Return) if ($Return) { return $Value }; 'continued'")]
    [InlineData("param([Collections.ArrayList]$Value,[bool]$Return) if ($Return) { $Value.Add('changed'); return $Value }; 'continued'")]
    [InlineData("param([bool]$Return) [string[]]$Value=@('alpha'); if ($Return) { return $Value }; 'continued'")]
    public void Transpile_ConditionalReturnKeepsUnprovedEnumerationMutationAndLocalOwnershipRetained(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UnprovedReturn { " + body + " }" + Environment.NewLine,
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "UnprovedConditionalReturn", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions,
            static region => region.SourceName == "Get-UnprovedReturn" && region.ControlFlowContract is not null);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$value = [ordered]@{}", "System.Collections.Specialized.OrderedDictionary", PowerShellRegionTransferOwnership.EarlierRegion)]
    [InlineData("[string[]]$value = @('alpha', 'beta')", "System.String[]", PowerShellRegionTransferOwnership.RetainedDefiniteAssignment)]
    [InlineData("$value = [Collections.ArrayList]::new()", "System.Collections.ArrayList", PowerShellRegionTransferOwnership.EarlierRegion)]
    [InlineData("$value = [Collections.Generic.List[object]]::new()", "System.Collections.Generic.List`1[[System.Object", PowerShellRegionTransferOwnership.EarlierRegion)]
    public void Transpile_HybridTransfersClosedCollectionFamiliesAroundRetainedPowerShell(
        string initializer,
        string expectedTypePrefix,
        PowerShellRegionTransferOwnership inputOwnership)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-CollectionBoundary {
                INITIALIZER
                data CollectionBarrier { }
                & { $null = $value.Count }
                $value
            }
            """.Replace("INITIALIZER", initializer, StringComparison.Ordinal), ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "CollectionBoundaryMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var regions = typed.PromotedRegions.OrderBy(static region => region.StartOffset).ToArray();

        Assert.True(regions.Length == 2,
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(static candidate =>
                candidate.StartLine + "-" + candidate.EndLine +
                " inputs=" + string.Join(",", candidate.InputLocals.Select(static local => local.Name + ":" + local.TypeName)) +
                " outputs=" + string.Join(",", candidate.ContinuationLocals.Select(static local => local.Name + ":" + local.TypeName)) +
                " " + candidate.DecisionCode + ": " + candidate.Reason)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.RegionOpportunities.SelectMany(static opportunity =>
                opportunity.LiveInputs.Concat(opportunity.LiveOutputs).Select(transfer =>
                    opportunity.StartLine + "-" + opportunity.EndLine + " " + transfer.Identity + ":" + transfer.TypeName))));
        Assert.True(regions[0].RequiresLocalOwnershipGuard);
        var output = Assert.Single(regions[0].ContinuationLocals);
        Assert.StartsWith(expectedTypePrefix, output.TypeName, StringComparison.Ordinal);
        Assert.Equal(PowerShellRegionTransferOwnership.GuardedFresh,
            Assert.IsType<PowerShellRegionTransferContract>(output.Contract).Ownership);
        var input = Assert.Single(regions[1].InputLocals);
        Assert.StartsWith(expectedTypePrefix, input.TypeName, StringComparison.Ordinal);
        Assert.Equal(inputOwnership,
            Assert.IsType<PowerShellRegionTransferContract>(input.Contract).Ownership);
        Assert.Empty(regions[1].ContinuationLocals);
    }
}
