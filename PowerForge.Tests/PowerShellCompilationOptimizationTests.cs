using PowerForge;
using Xunit;

namespace PowerForge.Tests;

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
        Assert.DoesNotContain("99", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }
}
