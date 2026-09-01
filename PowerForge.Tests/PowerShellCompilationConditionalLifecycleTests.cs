namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void RuntimeFreePipelineLifecycleCollectsNestedConditionalProcessOutput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Select-Value { [CmdletBinding()] [OutputType([int])] " +
            "param([Parameter(ValueFromPipeline)][int] $Value) " +
            "begin { [int] $Final = 10 } " +
            "process { if ($Value -gt 0) { if ($Value -eq 2) { $Value } else { 100 } } } " +
            "end { $Final } } " +
            "function Invoke-Select { param([int[]] $Values) $Values | Select-Value }",
            TestPath("pipeline-lifecycle-conditional-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Select-Value");
        Assert.Equal(typeof(int[]), lifecycle.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.Collection, lifecycle.OutputCardinality);
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(
            Assert.Single(lifecycle.Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        var outer = Assert.IsType<PowerShellBoundIfStatement>(Assert.Single(loop.Body.Statements));
        var inner = Assert.IsType<PowerShellBoundIfStatement>(Assert.Single(Assert.Single(outer.Clauses).Body.Statements));
        Assert.IsType<PowerShellBoundClrInvocationExpression>(
            Assert.IsType<PowerShellBoundExpressionStatement>(Assert.Single(Assert.Single(inner.Clauses).Body.Statements)).Expression);
        Assert.IsType<PowerShellBoundClrInvocationExpression>(
            Assert.IsType<PowerShellBoundExpressionStatement>(Assert.Single(inner.ElseBlock!.Statements)).Expression);

        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Select_Value").Source;
        Assert.Contains("if ((Value > 0))", source, StringComparison.Ordinal);
        Assert.Contains("if ((Value == 2))", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split(".Add(", StringSplitOptions.None).Length - 1);
        Assert.Contains(".ToArray();", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("process { if ($Value -gt 0) { $Value } else { 'text' } }")]
    [InlineData("process { while ($Value -gt 0) { $Value; break } }")]
    [InlineData("process { if ($Value -gt 0) { return $Value } }")]
    public void RuntimeFreePipelineLifecycleRejectsWiderNestedProcessOutput(string processBlock)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Select-Value {{ [CmdletBinding()] [OutputType([int])] " +
            $"param([Parameter(ValueFromPipeline)][int] $Value) begin {{ }} {processBlock} end {{ 10 }} }} " +
            "function Invoke-Select { 1, 2 | Select-Value }",
            TestPath("pipeline-lifecycle-nested-output-rejected.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Select_Value");
    }
}
