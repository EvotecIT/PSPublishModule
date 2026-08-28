using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationOptimizationTests
{
    [Fact]
    public void BoundOptimizerFoldsPureNestedConstants()
    {
        var bound = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse("function Get-OptimizedValue { return 0 }", "optimizer.psm1") }).Bound;
        var function = Assert.Single(bound.Functions);
        var original = Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(function.Body.Statements));
        var type = new PowerShellTypeFact(typeof(int), PowerShellTypeFactProvenance.Literal, "Test literal.");
        PowerShellBoundExpression Literal(int value) => new PowerShellBoundLiteralExpression(original.Span, value, type, PowerShellValueState.Known);
        var addition = new PowerShellBoundBinaryExpression(original.Span, PowerShellBoundBinaryOperator.Add, Literal(2), Literal(3), type);
        var multiplication = new PowerShellBoundBinaryExpression(original.Span, PowerShellBoundBinaryOperator.Multiply, addition, Literal(4), type);
        var body = new PowerShellBoundBlock(function.Body.Span, new PowerShellBoundStatement[]
        {
            new PowerShellBoundReturnStatement(original.Span, multiplication)
        });
        var program = bound.WithFunctions(new[] { function.WithBody(body) });

        var optimized = new PowerShellBoundOptimizer().Optimize(program);

        Assert.Equal(2, optimized.Evidence.ConstantExpressionsFolded);
        Assert.Equal(0, optimized.Evidence.DeadBranchesRemoved);
        Assert.True(optimized.Evidence.Changed);
        var returned = Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(optimized.Program.Functions).Body.Statements));
        var literal = Assert.IsType<PowerShellBoundLiteralExpression>(returned.Expression);
        Assert.Equal(20, literal.Value);
    }

    [Fact]
    public void BoundOptimizerRemovesAStaticallyUnreachableConditionalBranch()
    {
        var bound = new PowerShellSemanticCompilationPipeline().Compile(
            new[]
            {
                PowerShellSourceParser.Parse(
                    "function Get-Choice { param([bool] $Condition) if ($Condition) { return 99 } else { return 88 } }",
                    "dead-branch.psm1")
            }).Bound;
        var function = Assert.Single(bound.Functions);
        var conditional = Assert.IsType<PowerShellBoundIfStatement>(Assert.Single(function.Body.Statements));
        var falseCondition = new PowerShellBoundLiteralExpression(
            conditional.Clauses[0].Condition.Span,
            false,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Literal, "Test literal."),
            PowerShellValueState.Known);
        var rewritten = new PowerShellBoundIfStatement(
            conditional.Span,
            new[] { new PowerShellBoundConditionalClause(falseCondition, conditional.Clauses[0].Body) },
            conditional.ElseBlock);
        var program = bound.WithFunctions(new[] { function.WithBody(new PowerShellBoundBlock(function.Body.Span, new PowerShellBoundStatement[] { rewritten })) });

        var optimized = new PowerShellBoundOptimizer().Optimize(program);

        Assert.Equal(1, optimized.Evidence.DeadBranchesRemoved);
        var selectedReturn = Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(optimized.Program.Functions).Body.Statements));
        Assert.Equal(88, Assert.IsType<PowerShellBoundLiteralExpression>(selectedReturn.Expression).Value);
    }
}
