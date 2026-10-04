using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string AdditionalRegionClosureSource = """
        function New-ClosedList {
            [CmdletBinding()] param()
            $List = [System.Collections.ArrayList]::new()
            return , $List
        }
        function Invoke-AdditionalRegionClosure {
            [CmdletBinding()] param([string]$Seed)
            [string]$State = $Seed
            data FirstBarrier { }
            $State = "${State}:one"
            data SecondBarrier { }
            $State = "${State}:two"
            data ThirdBarrier { }
            return $State
        }
        function Get-AdditionalRegionCollection {
            [CmdletBinding()] param()
            $Items = New-ClosedList
            data CollectionBarrier { }
            , $Items
        }
        Export-ModuleMember -Function Invoke-AdditionalRegionClosure, Get-AdditionalRegionCollection
        """;

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_SelectsEveryClosedAdditionalRegionWithOneLocalFactoryContract()
    {
        using var fixture = ArtifactFixture.Create(AdditionalRegionClosureSource, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "AdditionalRegionClosureMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var regions = typed.PromotedRegions
            .Where(static region => region.SourceName == "Invoke-AdditionalRegionClosure")
            .OrderBy(static region => region.StartOffset)
            .ToArray();
        Assert.True(regions.Length == 4,
            System.Text.Json.JsonSerializer.Serialize(typed.RegionCandidates) + Environment.NewLine +
            System.Text.Json.JsonSerializer.Serialize(typed.RegionOpportunities) + Environment.NewLine +
            System.Text.Json.JsonSerializer.Serialize(typed.Diagnostics));

        var prefix = regions[0];
        Assert.True(prefix.RequiresLocalOwnershipGuard);
        Assert.Equal(new[] { "State" }, prefix.ContinuationLocals.Select(static local => local.Name));

        var collectionRegions = typed.PromotedRegions
            .Where(static region => region.SourceName == "Get-AdditionalRegionCollection")
            .OrderBy(static region => region.StartOffset)
            .ToArray();
        var collectionRegion = Assert.Single(collectionRegions);
        var factory = Assert.Single(collectionRegion.LocalCalls);
        Assert.Equal("New-ClosedList", factory.SourceName);
        Assert.Equal(PowerShellRegionTransferShape.ListSequence, factory.ResultContract.Shape);
        Assert.Equal(PowerShellRegionTransferOwnership.CompiledCalleeFresh, factory.ResultContract.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.NoEnumerate, factory.ResultContract.OutputBehavior);

        Assert.All(regions.Skip(1).Take(2), static region =>
        {
            var input = Assert.Single(region.InputLocals);
            var output = Assert.Single(region.ContinuationLocals);
            Assert.Equal("State", input.Name, ignoreCase: true);
            Assert.Equal("State", output.Name, ignoreCase: true);
            Assert.Equal(PowerShellRegionTransferDirection.LiveIn, input.Contract!.Direction);
            Assert.Equal(PowerShellRegionTransferDirection.LiveInOut, output.Contract!.Direction);
            Assert.Empty(region.LocalCalls);
        });

        var terminal = regions[3];
        Assert.Empty(terminal.ContinuationLocals);
        Assert.Equal("State", Assert.Single(terminal.InputLocals).Name, ignoreCase: true);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.Atomic,
            Assert.IsType<PowerShellRegionTransferContract>(terminal.TerminalTransferContract).OutputBehavior);
        Assert.Empty(terminal.LocalCalls);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_AdditionalRegionClosureSkipsUnprovedErrorRouteWithoutDiscardingLaterRegions()
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-SelectiveRegionClosure {
                [CmdletBinding()] param([string]$Seed)
                [string]$State = $Seed
                data PrefixBarrier { }
                $State = $State + ':unproved'
                $State = "${State}:proved"
                data LaterBarrier { }
                return $State
            }
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "SelectiveRegionClosureMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var regions = typed.PromotedRegions
            .Where(static region => region.SourceName == "Invoke-SelectiveRegionClosure")
            .OrderBy(static region => region.StartOffset)
            .ToArray();
        Assert.Equal(3, regions.Length);
        Assert.Contains(typed.RegionCandidates, static candidate =>
            candidate.SourceName == "Invoke-SelectiveRegionClosure" &&
            !candidate.Promoted &&
            candidate.DecisionCode == "region.error-route");
        Assert.DoesNotContain(regions, static region => region.RegionGraph.Regions
            .Any(graph => graph.Errors.Count > 0));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void AdditionalRegionClosure_PreservesCollectionIdentityScalarTransfersAndReuse(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create(AdditionalRegionClosureSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.AdditionalRegionClosure",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static item => item.Name == "Invoke-AdditionalRegionClosure");
        Assert.True(unit.RetainedHostedSource);
        Assert.Equal(5, result.Manifest.PromotedTypedRegions);

        const string probe = """
            $first = Invoke-AdditionalRegionClosure -Seed 2
            $second = Invoke-AdditionalRegionClosure -Seed 3
            $firstCollection = Get-AdditionalRegionCollection
            $secondCollection = Get-AdditionalRegionCollection
            [void]$firstCollection.Add('caller')
            [pscustomobject]@{
                phase = 'ordinary'
                first = $first
                second = $second
                collectionType = $firstCollection.GetType().FullName
                firstCollection = @($firstCollection)
                secondCollection = @($secondCollection)
                fresh = -not [object]::ReferenceEquals($firstCollection, $secondCollection)
            } | ConvertTo-Json -Depth 5 -Compress
            $stopped = @(1..20 | ForEach-Object { Invoke-AdditionalRegionClosure -Seed $_ } | Select-Object -First 1)
            $reused = Invoke-AdditionalRegionClosure -Seed 4
            [pscustomobject]@{ phase = 'stop-reuse'; stopped = $stopped.Count; reused = @($reused) } |
                ConvertTo-Json -Depth 5 -Compress
            Remove-Module $module -Force
            $module = Import-Module $modulePath -PassThru -Force
            $reimported = Invoke-AdditionalRegionClosure -Seed 5
            [pscustomobject]@{ phase = 'reimport'; values = @($reimported) } |
                ConvertTo-Json -Depth 5 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "additional-region-closure-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "additional-region-closure-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("2:one:two", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("System.Collections.ArrayList", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
