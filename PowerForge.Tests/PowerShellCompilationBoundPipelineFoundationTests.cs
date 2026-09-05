namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void EmptyFunctionFlowsThroughTheCompleteSemanticPipeline()
    {
        var document = PowerShellSourceParser.Parse("function Invoke-Empty { }", TestPath("empty.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(typeof(void), function.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.None, function.OutputCardinality);
        Assert.Equal(PowerShellExecutionDispositionKind.Typed, function.Disposition.Kind);
        var method = Assert.Single(result.Emitted.Methods);
        Assert.Contains("public static void Invoke_Empty()", method.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", method.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return 42", typeof(int), "return 42;")]
    [InlineData("'ready'", typeof(string), "return \"ready\";")]
    [InlineData("return $true", typeof(bool), "return true;")]
    public void LiteralFunctionsCarryTypeFactsThroughLowering(string body, Type expectedType, string expectedSource)
    {
        var document = PowerShellSourceParser.Parse($"function Get-Value {{ {body} }}", TestPath("literal.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });
        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(expectedType, function.ReturnType.ClrType);
        Assert.Equal(PowerShellTypeFactProvenance.Inferred, function.ReturnType.Provenance);
        Assert.Equal(PowerShellOutputCardinality.Scalar, function.OutputCardinality);
        var lowered = Assert.Single(result.Lowered.Functions);
        var method = Assert.Single(result.Emitted.Methods);
        Assert.Equal(lowered.Span, method.SourceSpan);
        Assert.Contains(expectedSource, method.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitParameterTypeFlowsThroughTheNeutralVariableNode()
    {
        var source = "function Get-Value { param([int] $Count) return $Count }";
        var document = PowerShellSourceParser.Parse(source, TestPath("parameter.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });
        var method = Assert.Single(result.Emitted.Methods);
        Assert.Equal(typeof(int), method.ReturnType);
        Assert.Contains("Get_Value(int Count)", method.Source, StringComparison.Ordinal);
        Assert.Contains("return Count;", method.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterAliasesBindingsAndValidationAreBoundBeforeLowering()
    {
        var source = "function Get-Value { param([Parameter(Mandatory, Position=0)] [Alias('c')] [ValidateRange(1, 9)] [int] $Count) return $Count }";
        var document = PowerShellSourceParser.Parse(source, TestPath("parameter-contract.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });
        var bound = Assert.Single(Assert.Single(result.Analyzed.Functions).Parameters);
        Assert.Equal(typeof(int), bound.Type.ClrType);
        Assert.Equal("Count", bound.Contract.Name);
        Assert.Equal(new[] { "c" }, bound.Contract.Aliases);
        Assert.True(bound.Contract.IsMandatory);
        Assert.Equal(0, Assert.Single(bound.Contract.Bindings).Position);
        var validation = Assert.Single(bound.Contract.Validations);
        Assert.Equal(PowerShellCompilationValidationKind.Range, validation.Kind);
        Assert.Equal(new[] { "1", "9" }, validation.Arguments);
        var lowered = Assert.Single(Assert.Single(result.Lowered.Functions).Parameters);
        Assert.Same(bound.Contract, lowered.Contract);
    }

    [Fact]
    public void PublicTranspilerUsesBoundPipelineForPlainTypedParametersAndAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "parameter.ps1");
        try
        {
            File.WriteAllText(path, "function Get-Count { param([Alias('c')] [int] $Count) return $Count }");
            var result = new PowerShellTypedCompilationTranspiler().Transpile(path);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            var method = Assert.Single(result.Methods);
            Assert.Equal(new[] { "c" }, Assert.Single(method.Parameters).Aliases);
            Assert.Contains("public static int Get_Count(int Count)", result.SourceCode, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("param([double] $Left, [double] $Right) return $Left + $Right", typeof(double), "return (Left + Right);")]
    [InlineData("param([bool] $Left, [bool] $Right) return $Left -and $Right", typeof(bool), "return (Left && Right);")]
    [InlineData("param([string] $Left, [string] $Right) return $Left -eq $Right", typeof(bool), "StringComparison.InvariantCultureIgnoreCase")]
    [InlineData("param([string] $Left, [string] $Right) return $Left -ceq $Right", typeof(bool), "StringComparison.InvariantCulture)")]
    [InlineData("param([int] $Value, [int] $Count) return $Value -shl $Count", typeof(int), "return (Value << (int)(Count));")]
    public void OperatorsCarryResolvedSemanticsThroughBoundAndLoweredNodes(string body, Type returnType, string expectedSource)
    {
        var document = PowerShellSourceParser.Parse($"function Get-Value {{ {body} }}", TestPath("operators.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });
        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(returnType, function.ReturnType.ClrType);
        Assert.IsType<PowerShellBoundBinaryExpression>(Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(function.Body.Statements)).Expression);
        Assert.IsType<PowerShellLoweredBinaryExpression>(Assert.IsType<PowerShellLoweredReturnStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements)).Expression);
        Assert.Contains(expectedSource, Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicTranspilerUsesBoundOperatorPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "operator.ps1");
        try
        {
            File.WriteAllText(path, "function Add-Value { param([double] $Left, [double] $Right) return $Left + $Right }");
            var result = new PowerShellTypedCompilationTranspiler().Transpile(path);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            Assert.Contains("public static double Add_Value(double Left, double Right)", result.SourceCode, StringComparison.Ordinal);
            Assert.Contains("return (Left + Right);", result.SourceCode, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
