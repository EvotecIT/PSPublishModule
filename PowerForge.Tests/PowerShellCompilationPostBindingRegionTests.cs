using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridDiscoversRegionWhenDefiniteAssignmentRetainsFunction()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Conditional { param([bool]$Use) if ($Use) { [int]$Seed = 1 }; return $Seed }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RegionalMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
        var opportunity = Assert.Single(typed.RegionOpportunities);
        Assert.Equal("Get-Conditional", opportunity.SourceName);
        Assert.True(opportunity.AnalysisOnly);
        Assert.Empty(typed.PromotedRegions);
        Assert.DoesNotContain("__PowerForgeOpportunity_", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridDiscoversRegionAfterCmdletParameterShapingRejectsFunction()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Scaled { param([int]$Verbose) [int]$Result = 2; $Result += $Verbose; return $Result }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RegionalMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(typed.Methods);
        Assert.Empty(typed.RegionOpportunities);

        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
            typed, exportedFunctions: null, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(prepared.Methods);
        Assert.Contains(prepared.Diagnostics, diagnostic => diagnostic.Message.Contains("common parameter", StringComparison.OrdinalIgnoreCase));
        var opportunity = Assert.Single(prepared.RegionOpportunities);
        Assert.Equal("Get-Scaled", opportunity.SourceName);
        Assert.Equal(3, opportunity.StatementCount);
        Assert.True(opportunity.LiveInputSourceAnalysisComplete);
        Assert.True(opportunity.LiveOutputConsumerAnalysisComplete);
        Assert.Equal(PowerShellCompilationRegionContinuation.Terminating, opportunity.Continuation);
        Assert.Contains(opportunity.LiveInputs, input => input.Identity == "Parameter:VERBOSE" && input.StableScalar);
        Assert.True(opportunity.AnalysisOnly);
        Assert.Empty(prepared.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridDiscoversRetainedFunctionWithResolvedLocalCallClosure()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Seed { param([int]$Value) if ($Value -gt 0) { return $Value }; return 2 }; function Get-Scaled { param([int]$Value) [int]$Result = Get-Seed -Value $Value; $Result += $Value; return $Result }",
            ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();
        var typed = transpiler.TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RegionalMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(2, typed.Methods.Length);
        Assert.Empty(typed.RegionOpportunities);
        var seed = Assert.Single(typed.Methods, method => method.SourceName == "Get-Scaled");
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(seed.SourcePath) + "\0" + seed.SourceName + "\0" + seed.SourceLine
        };

        var retained = transpiler.TranspileExcluding(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RegionalMethods", "net10.0", excluded,
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Equal("Get-Seed", Assert.Single(retained.Methods).SourceName);
        var caller = Assert.Single(retained.RegionOpportunities, opportunity => opportunity.SourceName == "Get-Scaled");
        Assert.Contains("Function:GET-SEED", caller.LocalCalls);
        Assert.True(caller.AnalysisOnly);
        Assert.Empty(retained.PromotedRegions);
    }
}
