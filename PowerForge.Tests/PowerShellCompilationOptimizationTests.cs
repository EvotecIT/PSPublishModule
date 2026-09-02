using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationOptimizationTests
{
    [Fact]
    public void SemanticPipelineFoldsAuthoredPureConstants()
    {
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse("function Get-OptimizedValue { return 1.5 + 2.5 }", "optimizer.psm1") });

        Assert.Empty(result.Emitted.Diagnostics);
        Assert.Equal(1, result.Optimization.ConstantExpressionsFolded);
        Assert.Equal(0, result.Optimization.DeadBranchesRemoved);
        Assert.True(result.Optimization.Changed);
        var returned = Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        var literal = Assert.IsType<PowerShellBoundLiteralExpression>(returned.Expression);
        Assert.Equal(4d, literal.Value);
        Assert.Contains("return 4d;", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticPipelineRemovesAuthoredStaticallyUnreachableConditionalBranch()
    {
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[]
            {
                PowerShellSourceParser.Parse(
                    "function Get-Choice { if ($false) { return 99 } else { return 88 } }",
                    "dead-branch.psm1")
            });

        Assert.Empty(result.Emitted.Diagnostics);
        Assert.Equal(1, result.Optimization.DeadBranchesRemoved);
        var selectedReturn = Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        Assert.Equal(88, Assert.IsType<PowerShellBoundLiteralExpression>(selectedReturn.Expression).Value);
        Assert.DoesNotContain("return 99;", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticPipelineSpecializesArrayLoopsAndEmitsAuthoredSequencePoints()
    {
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[]
            {
                PowerShellSourceParser.Parse(
                    "function Get-Sum { param([int[]] $Numbers) [long] $total = 0; foreach ($number in $Numbers) { $total += $number }; return $total }",
                    "array-loop.psm1")
            });

        Assert.Empty(result.Emitted.Diagnostics);
        Assert.Equal(1, result.Optimization.SpecializedCollectionLoops);
        Assert.True(result.Optimization.SourceMappedStatements >= 3);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("for (int __foreachIndex_", source, StringComparison.Ordinal);
        Assert.Contains("#line ", source, StringComparison.Ordinal);
        Assert.Contains("#line default", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticPipelineReportsFusedAndCoalescedHostedCommandWork()
    {
        var document = PowerShellSourceParser.Parse(
            "function Invoke-Hosted { Get-Item . | Select-Object Name; Get-Date -Format o }",
            "hosted-regions.psm1");
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            capabilities: PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics);
        Assert.True(result.Optimization.PipelineStagesFused >= 1);
        Assert.True(result.Optimization.CommandRegionStatementsCoalesced >= 1);
        Assert.Equal(1, Assert.Single(result.Emitted.Methods).HostedRegionSiteCount);
    }

    [Fact]
    public void BoundaryProfilerMeasuresEquivalentWorkAndCountsEveryCrossing()
    {
        var baselineCalls = 0;
        var boundaryCalls = 0;
        var profile = new PowerShellCompilationBoundaryProfiler().Profile(
            "bounded-profile",
            boundaryInvocationsPerIteration: 4,
            baselineOperation: () => baselineCalls++,
            boundaryOperation: () => boundaryCalls++,
            warmupIterations: 1,
            measuredIterations: 3);

        Assert.Equal(4, baselineCalls);
        Assert.Equal(4, boundaryCalls);
        Assert.Equal(12, profile.BoundaryInvocations);
        Assert.True(profile.BaselineDurationNanoseconds >= 0);
        Assert.True(profile.BoundaryDurationNanoseconds >= 0);
        Assert.False(string.IsNullOrWhiteSpace(profile.RuntimeIdentifier));
    }
}
