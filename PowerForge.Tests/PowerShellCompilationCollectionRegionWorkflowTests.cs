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
        Assert.Equal(2, result.Manifest!.PromotedTypedRegions);

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

        Assert.True(regions.Length == 2,
            string.Join(Environment.NewLine, typed.RegionCandidates.Where(static candidate => candidate.SourceName == "Split-Array")
                .Select(static candidate => candidate.StartLine + "-" + candidate.EndLine + " " + candidate.DecisionCode + ": " + candidate.Reason)));
        Assert.Equal(new[] { 44, 51 }, regions.Select(static region => region.StartLine));
        Assert.True(regions[0].RequiresLocalOwnershipGuard);
        var fresh = Assert.IsType<PowerShellRegionTransferContract>(Assert.Single(regions[0].ContinuationLocals).Contract);
        Assert.Equal(PowerShellRegionTransferShape.ListSequence, fresh.Shape);
        Assert.Equal(PowerShellRegionTransferDirection.LiveOut, fresh.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.GuardedFresh, fresh.Ownership);
        Assert.Empty(regions[1].ContinuationLocals);
        var input = Assert.Single(regions[1].InputLocals);
        Assert.Equal("outArray", input.Name, ignoreCase: true);
        var earlier = Assert.IsType<PowerShellRegionTransferContract>(input.Contract);
        Assert.Equal(PowerShellRegionTransferDirection.LiveIn, earlier.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.EarlierRegion, earlier.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.NoEnumerate,
            Assert.IsType<PowerShellRegionTransferContract>(regions[1].TerminalTransferContract).OutputBehavior);
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
