namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void RuntimeFreePipelineLifecycleLowersBeginProcessEndIntoOneCollectionInvocation()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Total { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { [int] $Total = 0 } process { $Total += $Value } end { $Total } } function Invoke-Measure { 40, 2 | Measure-Total }",
            TestPath("pipeline-lifecycle.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Measure-Total");
        var collectionParameter = Assert.Single(lifecycle.Parameters);
        Assert.Equal(typeof(int[]), collectionParameter.Type.ClrType);
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(
            Assert.Single(lifecycle.Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        Assert.True(loop.DeclareVariable);
        Assert.Equal("Value", loop.Variable.Name);
        var nullElement = Assert.IsType<PowerShellBoundLiteralExpression>(loop.NullCollectionElement);
        Assert.Equal(typeof(int), nullElement.Type.ClrType);
        Assert.Equal(0, nullElement.Value);
        Assert.IsType<PowerShellBoundAssignmentStatement>(lifecycle.Body.Statements[0]);
        Assert.True(Assert.IsType<PowerShellBoundExpressionStatement>(lifecycle.Body.Statements[^1]).EmitsOutput);
        var lifecycleSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Total").Source;
        Assert.Contains("Measure_Total(int[] __pf_pipeline_input_", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("?? new int[] { 0 }", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("int Total = 0;", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("int Value = __foreachItem_", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("Total = checked((int)(Total + Value));", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("return Total;", lifecycleSource, StringComparison.Ordinal);
        var callerSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure").Source;
        Assert.Contains("return Measure_Total(new int[] { 40, 2 });", callerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleAcceptsTypedArrayParameterInput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Total { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { [int] $Total = 0 } process { $Total += $Value } end { $Total } } " +
            "function Invoke-Measure { param([int[]] $Values) $Values | Measure-Total }",
            TestPath("pipeline-lifecycle-parameter.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var caller = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Invoke-Measure");
        var invocation = Assert.IsType<PowerShellBoundInvocationExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(caller.Body.Statements)).Expression);
        var input = Assert.IsType<PowerShellBoundVariableExpression>(Assert.Single(invocation.Arguments));
        Assert.Equal(typeof(int[]), input.Type.ClrType);
        var callerSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure").Source;
        Assert.Contains("return Measure_Total(Values);", callerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleRejectsScalarVariableInput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Total { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { [int] $Total = 0 } process { $Total += $Value } end { $Total } } " +
            "function Invoke-Measure { [int] $Value = 42; $Value | Measure-Total }",
            TestPath("pipeline-lifecycle-scalar.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2922");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleRejectsNullInputWithBindingErrorSemantics()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-Value { [CmdletBinding()] param([Parameter(ValueFromPipeline)][bool] $Value) begin { [int] $Result = 42 } process { } end { $Result } } " +
            "function Invoke-Test { param([bool[]] $Values) $Values | Test-Value }",
            TestPath("pipeline-lifecycle-null-binding-error.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Test_Value");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2927");
    }

    [Theory]
    [InlineData("param([Parameter(ValueFromPipeline)][int] $Value) begin { $Value } process { } end { 42 }")]
    [InlineData("param([Parameter(ValueFromPipeline)][int] $Value) begin { } process { return } end { 42 }")]
    [InlineData("param([Parameter(ValueFromPipelineByPropertyName)][int] $Value) begin { } process { } end { 42 }")]
    [InlineData("param([Parameter(ValueFromPipeline)][object] $Value) begin { } process { } end { 42 }")]
    public void RuntimeFreePipelineLifecycleRejectsUnboundedLifecycleShapes(string body)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Measure-Value {{ [CmdletBinding()] {body} }} function Invoke-Measure {{ return 42 | Measure-Value }}",
            TestPath("unsupported-pipeline-lifecycle.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleRejectsBeginSuccessOutput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Value { 1 } function Measure-Value { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { Write-Value } process { } end { 42 } } function Invoke-Measure { 42 | Measure-Value }",
            TestPath("pipeline-lifecycle-early-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2925");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleMaterializesOrderedProcessAndEndOutput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Select-Value { [CmdletBinding()] [OutputType([int])] param([Parameter(ValueFromPipeline)][int] $Value) begin { [int] $Final = 10 } process { $Value; $Value } end { $Final } } " +
            "function Invoke-Select { param([int[]] $Values) $Values | Select-Value }",
            TestPath("pipeline-lifecycle-process-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Select-Value");
        Assert.Equal(typeof(int[]), lifecycle.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.Collection, lifecycle.OutputCardinality);
        Assert.Equal(typeof(int), lifecycle.DeclaredOutputType);
        var caller = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Invoke-Select");
        var invocation = Assert.IsType<PowerShellBoundInvocationExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(caller.Body.Statements)).Expression);
        Assert.Equal(typeof(int[]), invocation.Type.ClrType);

        var lifecycleSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Select_Value").Source;
        Assert.Contains("public static int[] Select_Value(int[] __pf_pipeline_input_", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<int>()", lifecycleSource, StringComparison.Ordinal);
        Assert.Equal(2, lifecycleSource.Split(".Add(Value)", StringSplitOptions.None).Length - 1);
        Assert.Contains(".Add(Final)", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains(".ToArray();", lifecycleSource, StringComparison.Ordinal);
        var callerSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Select").Source;
        Assert.Contains("return Select_Value(Values);", callerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleKeepsOutputFreeProcessInvocationOnScalarAbi()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Values { [CmdletBinding()] [OutputType([int])] param([Parameter(ValueFromPipeline)][int] $Value) " +
            "begin { [System.Collections.Generic.List[int]] $Items = [System.Collections.Generic.List[int]]::new() } " +
            "process { $Items.Add($Value) } end { $Items.Count } } " +
            "function Invoke-Measure { 1, 2 | Measure-Values }",
            TestPath("pipeline-lifecycle-output-free-invocation.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Measure-Values");
        Assert.Equal(typeof(int), lifecycle.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.Scalar, lifecycle.OutputCardinality);
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(
            Assert.Single(lifecycle.Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        var process = Assert.IsType<PowerShellBoundExpressionStatement>(Assert.Single(loop.Body.Statements));
        Assert.False(process.EmitsOutput);
        Assert.Equal(nameof(List<int>.Add), Assert.IsType<PowerShellBoundClrInvocationExpression>(process.Expression).MemberName);
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Values").Source;
        Assert.DoesNotContain("__pf_pipeline_output_", source, StringComparison.Ordinal);
        Assert.Contains(".Add(Value)", source, StringComparison.Ordinal);
        var caller = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure").Source;
        Assert.Contains("return Measure_Values(new int[] { 1, 2 });", caller, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleKeepsOutputFreeProcessStreamOnScalarAbi()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Values { [CmdletBinding()] [OutputType([int])] param([Parameter(ValueFromPipeline)][int] $Value) " +
            "begin { [int] $Total = 0 } process { Write-Warning 'seen'; $Total += $Value } end { $Total } } " +
            "function Invoke-Measure { 1, 2 | Measure-Values }",
            TestPath("pipeline-lifecycle-output-free-stream.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable | PowerShellCompilationCapability.PowerShellStreams);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Measure-Values");
        Assert.Equal(typeof(int), lifecycle.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.Scalar, lifecycle.OutputCardinality);
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(
            Assert.Single(lifecycle.Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        Assert.IsType<PowerShellBoundStreamWriteStatement>(loop.Body.Statements[0]);
        Assert.IsType<PowerShellBoundAssignmentStatement>(loop.Body.Statements[1]);
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Values").Source;
        Assert.DoesNotContain("__pf_pipeline_output_", source, StringComparison.Ordinal);
        Assert.Contains("__writeWarning(", source, StringComparison.Ordinal);
        Assert.Contains("\"seen\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleMaterializesTypedBinaryProcessOutput()
    {
        var document = PowerShellSourceParser.Parse(
            "function Select-Value { [CmdletBinding()] [OutputType([double])] param([Parameter(ValueFromPipeline)][double] $Value) begin { } process { $Value + 1.0 } end { 10.0 } } " +
            "function Invoke-Select { 1.0, 2.0 | Select-Value }",
            TestPath("pipeline-lifecycle-binary-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycle = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Select-Value");
        Assert.Equal(typeof(double[]), lifecycle.ReturnType.ClrType);
        Assert.Equal(PowerShellOutputCardinality.Collection, lifecycle.OutputCardinality);
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(
            Assert.Single(lifecycle.Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        var collected = Assert.IsType<PowerShellBoundClrInvocationExpression>(
            Assert.IsType<PowerShellBoundExpressionStatement>(Assert.Single(loop.Body.Statements)).Expression);
        var binary = Assert.IsType<PowerShellBoundBinaryExpression>(Assert.Single(collected.Arguments));
        Assert.Equal(typeof(double), binary.Type.ClrType);
        Assert.Equal(PowerShellBoundBinaryOperator.Add, binary.Operation);
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Select_Value").Source;
        Assert.Equal(2, source.Split(".Add(", StringSplitOptions.None).Length - 1);
        Assert.Contains(".ToArray();", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("process { 'text' }")]
    [InlineData("process { 1, 2 }")]
    public void RuntimeFreePipelineLifecycleRejectsNonHomogeneousOrCollectionProcessOutput(string processBlock)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Measure-Value {{ [CmdletBinding()] [OutputType([int])] param([Parameter(ValueFromPipeline)][int] $Value) begin {{ }} {processBlock} end {{ 42 }} }} function Invoke-Measure {{ 1, 2 | Measure-Value }}",
            TestPath("pipeline-lifecycle-process-output-rejected.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2928");
    }

    [Theory]
    [InlineData("end { }")]
    [InlineData("end { [int] $Result = 42 }")]
    [InlineData("end { 40; 2 }")]
    public void RuntimeFreePipelineLifecycleRejectsMissingOrMultipleEndOutput(string endBlock)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Measure-Value {{ [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin {{ }} process {{ }} {endBlock} }} function Invoke-Measure {{ 42 | Measure-Value }}",
            TestPath("pipeline-lifecycle-end-output.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleRejectsTerminalCallToOutputFreeHelper()
    {
        var document = PowerShellSourceParser.Parse(
            "function Invoke-VoidHelper { [int] $Local = 1 } function Measure-Value { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { } process { } end { Invoke-VoidHelper } } function Invoke-Measure { 42 | Measure-Value }",
            TestPath("pipeline-lifecycle-void-helper.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value");
        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2925");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleRejectsStreamRedirection()
    {
        var document = PowerShellSourceParser.Parse(
            "function Measure-Value { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin { } process { } end { 42 } } function Invoke-Measure { 42 | Measure-Value > $null }",
            TestPath("pipeline-lifecycle-redirection.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Measure");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2926");
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleCollectionNameCannotCollideWithAuthoredSymbols()
    {
        const string marker = "__PIPELINE_COLLECTION_COLLISION__";
        var template = $"function Measure-Value {{ [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) begin {{ [int] ${marker} = 0 }} process {{ ${marker} += $Value }} end {{ ${marker} }} }} function Invoke-Measure {{ 40, 2 | Measure-Value }}";
        var templateDocument = PowerShellSourceParser.Parse(template, TestPath("pipeline-lifecycle-collision-template.ps1"));
        var function = Assert.IsType<System.Management.Automation.Language.FunctionDefinitionAst>(Assert.Single(
            templateDocument.SyntaxRoot.FindAll(static node => node is System.Management.Automation.Language.FunctionDefinitionAst, searchNestedScriptBlocks: false),
            static node => ((System.Management.Automation.Language.FunctionDefinitionAst)node).Name == "Measure-Value"));
        var offset = Assert.Single(function.Body.ParamBlock!.Parameters).Extent.StartOffset;
        var collisionName = "__pf_pipeline_input_" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var document = PowerShellSourceParser.Parse(
            template.Replace(marker, collisionName, StringComparison.Ordinal),
            TestPath("pipeline-lifecycle-collision.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lifecycleSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Measure_Value").Source;
        Assert.Contains($"Measure_Value(int[] {collisionName}_1)", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains($"int {collisionName} = 0;", lifecycleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreePipelineLifecycleOutputCollectorCannotCollideWithAuthoredSymbols()
    {
        const string marker = "__PIPELINE_OUTPUT_COLLISION__";
        var template = $"function Select-Value {{ [CmdletBinding()] [OutputType([int])] param([Parameter(ValueFromPipeline)][int] $Value) begin {{ [int] ${marker} = 10 }} process {{ $Value; $Value }} end {{ ${marker} }} }} function Invoke-Select {{ 1, 2 | Select-Value }}";
        var templateDocument = PowerShellSourceParser.Parse(template, TestPath("pipeline-lifecycle-output-collision-template.ps1"));
        var function = Assert.IsType<System.Management.Automation.Language.FunctionDefinitionAst>(Assert.Single(
            templateDocument.SyntaxRoot.FindAll(static node => node is System.Management.Automation.Language.FunctionDefinitionAst, searchNestedScriptBlocks: false),
            static node => ((System.Management.Automation.Language.FunctionDefinitionAst)node).Name == "Select-Value"));
        var offset = Assert.Single(function.Body.ParamBlock!.Parameters).Extent.StartOffset;
        var collisionName = "__pf_pipeline_output_" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var document = PowerShellSourceParser.Parse(
            template.Replace(marker, collisionName, StringComparison.Ordinal),
            TestPath("pipeline-lifecycle-output-collision.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Select_Value").Source;
        Assert.Contains($"int {collisionName} = 10;", source, StringComparison.Ordinal);
        Assert.Contains($"global::System.Collections.Generic.List<int> {collisionName}_1 =", source, StringComparison.Ordinal);
        Assert.Contains(".ToArray();", source, StringComparison.Ordinal);
    }
}
