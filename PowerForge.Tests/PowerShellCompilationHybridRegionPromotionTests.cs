using PowerForge;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string HybridRegionSource = """
        function Get-RegionalValue {
            [CmdletBinding()]
            param([int] $Value, [bool] $UseZero)
            & { "hosted:$Value" }
            trap { continue }
            if ($UseZero) { return 0 }
            return $Value
        }
        Export-ModuleMember -Function Get-RegionalValue
        """;

    [Fact]
    public void Transpile_HybridPromotesBoundTerminalRegionWithoutClaimingWholeFunctionEmission()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();

        var hybrid = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var strict = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);
        var stateWithoutRegionAuthority = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule |
            PowerShellCompilationCapability.PowerShellModuleState);

        Assert.Empty(hybrid.Methods);
        var region = Assert.Single(hybrid.PromotedRegions);
        Assert.Equal("Get-RegionalValue", region.SourceName);
        Assert.Equal(typeof(int).FullName, region.ReturnType);
        Assert.Equal(new[] { "Value", "UseZero" }, region.InputParameters.Select(static parameter => parameter.Name));
        Assert.StartsWith("__PowerForgeRegion_", region.GeneratedName, StringComparison.Ordinal);
        Assert.Contains("public static int " + region.GeneratedName + "(int Value, bool UseZero)", hybrid.SourceCode, StringComparison.Ordinal);
        Assert.Equal(new[] { "Parameter:USEZERO", "Parameter:VALUE" }, Assert.Single(region.RegionGraph.Regions).Inputs);
        Assert.Equal(new[] { "Success" }, Assert.Single(region.RegionGraph.Regions).Streams);
        Assert.Empty(Assert.Single(region.RegionGraph.Regions).Errors);
        var accepted = Assert.Single(hybrid.RegionCandidates);
        Assert.True(accepted.Promoted);
        Assert.Equal("region.promoted", accepted.DecisionCode);
        Assert.Equal(region.RegionId, accepted.RegionId);
        Assert.Equal(region.GeneratedName, accepted.GeneratedName);
        Assert.NotNull(accepted.RegionGraph);
        Assert.Equal(3, hybrid.IrSnapshots!.SchemaVersion);
        Assert.Contains(hybrid.IrSnapshots.Bound, static unit => unit.Disposition == "PromotedTypedRegion");
        Assert.Contains(hybrid.IrSnapshots.Lowered, static unit => unit.Disposition == "PromotedTypedRegion");
        Assert.Empty(strict.PromotedRegions);
        Assert.Empty(strict.RegionCandidates);
        Assert.Empty(stateWithoutRegionAuthority.PromotedRegions);
        Assert.Empty(stateWithoutRegionAuthority.RegionCandidates);

        var census = new PowerShellCompilationCensusRunner().Run(new[] { fixture.ScriptPath }, "net10.0");
        var product = Assert.Single(census.Products);
        Assert.Equal(0, product.Coverage.EmittedFunctions);
        Assert.Equal(1, product.PromotedTypedRegions);
        Assert.Equal(0, product.RejectedTypedRegions);
        Assert.True(Assert.Single(product.RegionCandidates).Promoted);
        Assert.Equal(1, census.PromotedTypedRegions);
        Assert.Equal(1, Assert.Single(product.FunctionDispositions).PromotedTypedRegions);
    }

    [Fact]
    public void Transpile_HybridRejectsTerminalRegionWithModeledClrFailureRoute()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-RegionalValue {
                [CmdletBinding()]
                param([string] $Value, [bool] $UseZero)
                & { "hosted:$Value" }
                trap { continue }
                if ($UseZero) { return 0.0 }
                return [double] $Value
            }
            """,
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.Methods);
        Assert.Empty(typed.PromotedRegions);
        var decision = Assert.Single(typed.RegionCandidates);
        Assert.False(decision.Promoted);
        Assert.Equal("region.error-route", decision.DecisionCode);
        Assert.Contains("ClrException", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("PowerShellLanguageRuntimeError", decision.Reason, StringComparison.Ordinal);
        Assert.NotNull(decision.RegionGraph);
        Assert.DoesNotContain("__PowerForgeRegion_", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_HybridExplainsHostedCommandInsideTerminalCandidate()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegionalValue { [CmdletBinding()] param([object] $Status, [bool] $Warn); Write-Verbose \"Status: $Status\"; if ($Warn) { Write-Verbose 'suffix' }; return $true }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.PromotedRegions);
        var decision = Assert.Single(typed.RegionCandidates);
        Assert.False(decision.Promoted);
        Assert.Equal("region.command-boundary", decision.DecisionCode);
        Assert.Contains("hosted command boundary", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(decision.RegionGraph);
        Assert.Empty(decision.GeneratedName);
    }

    [Fact]
    public void Transpile_HybridReportsGeneratedNameCollisionAsRetained()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();
        var initial = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var helperName = Assert.Single(initial.PromotedRegions).GeneratedName;
        File.AppendAllText(fixture.ScriptPath, $"{Environment.NewLine}function {helperName} {{ return 7 }}");

        var typed = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.PromotedRegions);
        Assert.Contains(typed.Methods, method => method.GeneratedName == helperName);
        var candidate = Assert.Single(typed.RegionCandidates);
        Assert.False(candidate.Promoted);
        Assert.Equal("region.generated-name-collision", candidate.DecisionCode);
        Assert.Empty(candidate.GeneratedName);
        Assert.Contains(typed.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("collides with generated CLR method name", StringComparison.Ordinal));
    }

    [Fact]
    public void Census_RegionCandidateEvidenceRoundTripsThroughPublicJsonContract()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var original = new PowerShellCompilationCensusRunner().Run(new[] { fixture.ScriptPath }, "net10.0");

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<PowerShellCompilationCensusResult>(json);

        var candidate = Assert.Single(Assert.Single(roundTripped!.Products).RegionCandidates);
        Assert.True(candidate.Promoted);
        Assert.Equal("region.promoted", candidate.DecisionCode);
        Assert.NotNull(candidate.RegionGraph);
    }

    [Fact]
    public void Transpile_HybridExplainsWhyLiveLocalTransferFailsClosed()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-RegionalValue {
                param([int] $Value)
                if ($Value -gt 0) {
                    $Saved = 'positive'
                    Write-Verbose 'hosted'
                } else {
                    $Saved = 'other'
                    Write-Verbose 'hosted'
                }
                return "$Saved"
            }
            """,
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.Methods);
        Assert.Empty(typed.PromotedRegions);
        var decision = Assert.Single(typed.RegionCandidates);
        Assert.False(decision.Promoted);
        Assert.Equal("PSD1001", decision.DecisionCode);
        Assert.Contains("definitely assigned", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(decision.RegionGraph);
        Assert.DoesNotContain("__PowerForgeRegion_", typed.SourceCode, StringComparison.Ordinal);

        var census = new PowerShellCompilationCensusRunner().Run(new[] { fixture.ScriptPath }, "net10.0");
        var product = Assert.Single(census.Products);
        Assert.Equal(1, product.RejectedTypedRegions);
        Assert.Equal("PSD1001", Assert.Single(product.RegionCandidates).DecisionCode);
    }

    [Fact]
    public void Transpile_HybridDiscoversNonTerminalTypedOpportunitiesWithoutEmittingThem()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-RegionalValue {
                param([int] $Value)
                [int] $Seed = 1
                $Seed += $Value
                [int] $Scaled = 2
                $Scaled += $Seed
                & { "hosted:$Scaled" }
                [int] $Final = 3
                $Final += $Scaled
                return $Final
            }
            """,
            ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();

        var hybrid = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var strict = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(hybrid.Methods);
        Assert.Empty(hybrid.PromotedRegions);
        Assert.DoesNotContain("__PowerForgeOpportunity_", hybrid.SourceCode, StringComparison.Ordinal);
        var opportunities = hybrid.RegionOpportunities.OrderBy(static item => item.StartOffset).ToArray();
        Assert.Equal(2, opportunities.Length);
        var prefix = opportunities[0];
        Assert.Equal(4, prefix.StatementCount);
        Assert.Equal(PowerShellCompilationRegionContinuation.UnboundFallThrough, prefix.Continuation);
        Assert.False(prefix.ContinuationAnalysisComplete);
        Assert.True(prefix.LiveInputSourceAnalysisComplete);
        Assert.False(prefix.LiveOutputConsumerAnalysisComplete);
        Assert.False(prefix.InsideTerminalCandidate);
        Assert.Contains(prefix.LiveInputs, static input => input.Identity == "Parameter:VALUE" && input.StableScalar);
        Assert.Equal(2, prefix.LiveOutputs.Count);
        Assert.Contains(prefix.LiveOutputs, static output => output.Identity == "Local:SEED" && output.StableScalar);
        Assert.Contains(prefix.LiveOutputs, static output => output.Identity == "Local:SCALED" && output.StableScalar);
        Assert.Empty(prefix.LocalCalls);
        Assert.Equal(PowerShellCompilationRegionExecution.Typed, Assert.Single(prefix.RegionGraph.Regions).Execution);
        Assert.Equal(0, prefix.RegionGraph.StaticBoundaryCostUnits);
        var sourceText = File.ReadAllText(fixture.ScriptPath);
        var prefixText = sourceText.Substring(prefix.StartOffset, prefix.EndOffset - prefix.StartOffset);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefixText))).ToLowerInvariant(),
            prefix.SourceSha256);
        var suffix = opportunities[1];
        Assert.Equal(3, suffix.StatementCount);
        Assert.Equal(PowerShellCompilationRegionContinuation.Terminating, suffix.Continuation);
        Assert.True(suffix.ContinuationAnalysisComplete);
        Assert.False(suffix.LiveInputSourceAnalysisComplete);
        Assert.True(suffix.LiveOutputConsumerAnalysisComplete);
        Assert.True(suffix.InsideTerminalCandidate);
        Assert.Contains(suffix.LiveInputs, static input => input.Identity == "Local:SCALED" && input.StableScalar);
        Assert.Empty(suffix.LiveOutputs);
        Assert.Empty(strict.RegionOpportunities);

        var census = new PowerShellCompilationCensusRunner().Run(new[] { fixture.ScriptPath }, "net10.0");
        var json = JsonSerializer.Serialize(census);
        var roundTripped = JsonSerializer.Deserialize<PowerShellCompilationCensusResult>(json);
        Assert.Equal(2, Assert.Single(roundTripped!.Products).RegionOpportunities.Length);
        var legacyJson = JsonNode.Parse(json)!.AsObject();
        Assert.True(legacyJson["Products"]!.AsArray()[0]!.AsObject().Remove("RegionOpportunities"));
        var legacyRoundTrip = JsonSerializer.Deserialize<PowerShellCompilationCensusResult>(legacyJson.ToJsonString());
        Assert.Empty(Assert.Single(legacyRoundTrip!.Products).RegionOpportunities);
    }

    [Fact]
    public void Transpile_HybridRejectsIsolatedRegionWhoseLocalCallClosureIsNotPromoted()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Helper { return 1 }; function Get-RegionalValue { & { 'hosted' }; trap { continue }; return Get-Helper }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Single(typed.Methods, static method => method.SourceName == "Get-Helper");
        Assert.Empty(typed.PromotedRegions);
        Assert.DoesNotContain("__PowerForgeRegion_", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_HybridReportsResolvedLocalCallOpportunityWithoutEmittingIt()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Helper { return 1 }; function Get-RegionalValue { data HostedData { 'hosted' }; return Get-Helper }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Single(typed.Methods, static method => method.SourceName == "Get-Helper");
        Assert.Empty(typed.PromotedRegions);
        var opportunity = Assert.Single(typed.RegionOpportunities);
        Assert.Contains(opportunity.LocalCalls, static call => call.EndsWith("GET-HELPER", StringComparison.Ordinal));
        Assert.True(opportunity.AnalysisOnly);
        Assert.DoesNotContain("__PowerForgeOpportunity_", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_HybridRebindingReplacesSupersededRegionOpportunities()
    {
        using var fixture = ArtifactFixture.Create(
            "function Read-State { return $script:State }; " +
            "function Get-StateAccess { [object]$Value = Read-State; return $Value.Count; data HostedData { 'hosted' } }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.RegionOpportunities);
        Assert.Empty(typed.RegionCandidates);
        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Get-StateAccess");
    }

    [Fact]
    public void Transpile_HybridOpportunityRetainsAuthoredStatementIdentityAfterOptimization()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-OptimizedOpportunity { param([int]$Value) if ($true) { $Value = 1 }; $Value++; data HostedData { 'hosted' } }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var opportunity = Assert.Single(typed.RegionOpportunities);
        Assert.Equal(0, opportunity.StartStatementIndex);
        Assert.Equal(1, opportunity.EndStatementIndex);
        Assert.Equal(2, opportunity.StatementCount);
        var region = Assert.Single(opportunity.RegionGraph.Regions);
        Assert.Equal(opportunity.StartOffset, region.StartOffset);
        Assert.Equal(opportunity.EndOffset, region.EndOffset);
        var source = File.ReadAllText(fixture.ScriptPath);
        var exactSource = source.Substring(opportunity.StartOffset, opportunity.EndOffset - opportunity.StartOffset);
        Assert.StartsWith("if ($true)", exactSource, StringComparison.Ordinal);
        Assert.Contains("$Value++", exactSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_HybridOpportunityOmitsAuthoredStatementContainingMixedOptimizedRegions()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MixedOptimizedOpportunity { param([int]$Value) " +
            "if ($true) { $Value = 1; $script:State = $Value; $Value++ }; data HostedData { 'hosted' } }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.RegionOpportunities);
        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Get-MixedOptimizedOpportunity");
    }

    [Fact]
    public void Transpile_HybridOpportunityStopsContinuationAtFirstTerminatingStatement()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TerminatingOpportunity { return 1; [int]$Never = 2; data HostedData { 'hosted' } }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var opportunity = Assert.Single(typed.RegionOpportunities);
        Assert.Equal(2, opportunity.StatementCount);
        Assert.Equal(PowerShellCompilationRegionContinuation.Terminating, opportunity.Continuation);
        Assert.True(opportunity.ContinuationAnalysisComplete);
        Assert.True(opportunity.LiveOutputConsumerAnalysisComplete);
    }

    [Fact]
    public void Census_FailsBaselineWhenAFunctionLosesItsPromotedTypedRegion()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var runner = new PowerShellCompilationCensusRunner();
        var baseline = runner.Run(new[] { fixture.ScriptPath }, "net10.0");
        Assert.Equal(1, baseline.PromotedTypedRegions);
        File.WriteAllText(
            fixture.ScriptPath,
            HybridRegionSource.Replace("return $Value", "return $Value + 1", StringComparison.Ordinal));

        var compared = runner.Run(new[] { fixture.ScriptPath }, "net10.0", baseline);

        Assert.Contains(compared.Regressions, static regression =>
            regression.Metric.StartsWith("PromotedTypedRegionsFunction:", StringComparison.Ordinal));
        Assert.False(compared.Passed);
    }

    [Fact]
    public void ComposeRoot_FailsClosedWhenPromotedRegionSourceChangesAfterSelection()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(typed.PromotedRegions);
        File.WriteAllText(fixture.ScriptPath, HybridRegionSource.Replace("return $Value", "return 9", StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(() => PowerShellHybridModuleComposer.ComposeRoot(
            fixture.ScriptPath,
            "Regional.dll",
            typed));

        Assert.Contains("Promoted region", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeRoot_FailsClosedWhenPromotedRegionFunctionHeaderChangesAfterSelection()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RegionalMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(typed.PromotedRegions);
        File.WriteAllText(fixture.ScriptPath, HybridRegionSource.Replace("[int] $Value", "[string] $Value", StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(() => PowerShellHybridModuleComposer.ComposeRoot(
            fixture.ScriptPath,
            "Regional.dll",
            typed));

        Assert.Contains("Promoted region", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HybridNamedLifecycleRemainsOneHostedUnitWithoutRegionPromotion()
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-Lifecycle {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][int] $Value)
                begin { & { 'begin' } }
                process { $Value }
                end {
                    trap { continue }
                    [int] $endValue = $Value
                    return $endValue
                }
                clean { if ($null -ne $endValue) { $endValue } }
            }
            Export-ModuleMember -Function Invoke-Lifecycle
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridLifecycleRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.PromotedTypedRegions);
        Assert.DoesNotContain("__PowerForgeRegion_", File.ReadAllText(result.ArtifactPath!), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridPromotedRegionPreservesRuntimeOutputAndReportsPartialEmissionHonestly()
    {
        using var fixture = ArtifactFixture.Create(HybridRegionSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true,
            EmitIrSnapshots = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.PromotedTypedRegions);
        Assert.Equal(3, result.Manifest.Boundaries!.SchemaVersion);
        Assert.Equal(1, result.Manifest.Boundaries.PromotedTypedRegions);
        Assert.Equal(1, result.Manifest.Boundaries.StaticBoundarySites);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        Assert.Equal(4, ledger.SchemaVersion);
        Assert.Equal(1, ledger.PromotedTypedRegions);
        var entry = Assert.Single(ledger.Entries, static candidate => candidate.Name == "Get-RegionalValue");
        Assert.False(entry.EmittedClrMethod);
        Assert.False(entry.EmittedBinaryCmdlet);
        Assert.True(entry.RetainedHostedSource);
        Assert.True(entry.RuntimeRouted);
        Assert.Equal(1, entry.PromotedTypedRegions);
        Assert.Equal(1, entry.BoundaryCrossings);
        Assert.Equal(0, entry.ModuleStateReadBoundaryCrossings);
        Assert.Equal(0, entry.ModuleStateWriteBoundaryCrossings);
        Assert.Equal(string.Empty, entry.GeneratedMemberName);
        Assert.Contains("TypedRegions", entry.ArtifactDisposition, StringComparison.Ordinal);
        Assert.Contains("HostedSource", entry.ArtifactDisposition, StringComparison.Ordinal);
        Assert.Single(entry.GeneratedRegionMemberNames);
        var trace = Assert.IsType<PowerShellCompilationExplanation>(result.Manifest.DecisionTrace);
        Assert.Equal(5, trace.SchemaVersion);
        Assert.Equal(4, trace.SemanticCompatibilityVersion);
        Assert.Equal(1, trace.PromotedTypedRegions);
        var unit = Assert.Single(Assert.Single(trace.Files).Units, static candidate => candidate.Name == "Get-RegionalValue");
        Assert.Equal(PowerShellCompilationDecisionKind.RuntimeFallback, unit.Decision);
        Assert.Equal("BoundClrRegions+PowerShellRuntime", unit.LoweringRoute);
        Assert.Equal(1, unit.PromotedTypedRegions);
        var generatedMaps = result.Manifest.FailureMap!.Entries.Where(item =>
            item.GeneratedMemberName == entry.GeneratedRegionMemberNames[0] && item.GeneratedStartLine > 0).ToArray();
        Assert.NotEmpty(generatedMaps);
        var generatedMap = generatedMaps
            .OrderBy(static item => item.SourceEndLine - item.SourceStartLine)
            .ThenBy(static item => item.SourceStartLine)
            .First();
        var retainedMap = Assert.Single(result.Manifest.FailureMap.Entries, item =>
            item.UnitId == entry.UnitId && item.GeneratedMemberName.Length == 0 && item.GeneratedStartLine == 0);
        Assert.True(retainedMap.SourceStartLine < generatedMap.SourceStartLine);
        var prefixFailure = PowerShellCompilationFailureMapper.MapRuntimeFailure(
            result.Manifest,
            $"failure in {fixture.ScriptPath}:line {retainedMap.SourceStartLine}");
        Assert.Equal(retainedMap.SourceStartLine, Assert.Single(prefixFailure.Locations).Line);
        var regionFailure = PowerShellCompilationFailureMapper.MapRuntimeFailure(
            result.Manifest,
            $"failure in {fixture.ScriptPath}:line {generatedMap.SourceStartLine}");
        Assert.Equal(generatedMap.SourceStartLine, Assert.Single(regionFailure.Locations).Line);
        Assert.True(result.Manifest.IrSnapshots!.Emitted);
        var falsePathProof = "Get-RegionalValue -Value 7";
        var truePathProof = "Get-RegionalValue -Value 7 -UseZero $true";
        Assert.Equal(
            RunModuleProof(fixture.ScriptPath, falsePathProof),
            RunModuleProof(result.ArtifactPath!, falsePathProof));
        Assert.Equal(
            RunModuleProof(fixture.ScriptPath, truePathProof),
            RunModuleProof(result.ArtifactPath!, truePathProof));
        Assert.Equal(new[] { "hosted:7", "7" },
            RunModuleProof(result.ArtifactPath!, falsePathProof).Split(Environment.NewLine));
        Assert.Equal(new[] { "hosted:7", "0" },
            RunModuleProof(result.ArtifactPath!, truePathProof).Split(Environment.NewLine));
        var generatedModule = File.ReadAllText(result.ArtifactPath!);
        Assert.Contains("::" + entry.GeneratedRegionMemberNames[0] + "(${Value}, ${UseZero})", generatedModule, StringComparison.Ordinal);
        Assert.DoesNotContain("return $Value", generatedModule, StringComparison.Ordinal);
        PowerShellCompilationArtifactEvidence.Validate(result.Manifest);
        result.Manifest.PromotedTypedRegions = 0;
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationArtifactEvidence.Validate(result.Manifest));
    }
}
