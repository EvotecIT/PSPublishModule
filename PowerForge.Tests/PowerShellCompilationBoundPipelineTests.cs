namespace PowerForge.Tests;

public sealed class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void EmptyFunctionFlowsThroughTheCompleteSemanticPipeline()
    {
        var document = PowerShellSourceParser.Parse("function Invoke-Empty { }", TestPath("empty.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(typeof(void), function.ReturnType.ClrType);
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
        Assert.Contains(expectedSource, Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
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

    [Fact]
    public void ConditionalAndLoopControlFlowUseNestedBoundBlocks()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Sign { param([double] $Value, [bool] $Repeat) if ($Value -gt 0.0) { return 1.0 } else { while ($Repeat) { break }; return -1.0 } }",
            TestPath("control-flow.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(typeof(double), function.ReturnType.ClrType);
        var conditional = Assert.IsType<PowerShellBoundIfStatement>(Assert.Single(function.Body.Statements));
        Assert.IsType<PowerShellBoundBinaryExpression>(Assert.Single(conditional.Clauses).Condition);
        Assert.IsType<PowerShellBoundWhileStatement>(Assert.Single(conditional.ElseBlock!.Statements, static statement => statement is PowerShellBoundWhileStatement));
        var lowered = Assert.IsType<PowerShellLoweredIfStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements));
        Assert.IsType<PowerShellLoweredWhileStatement>(Assert.Single(lowered.ElseStatements!, static statement => statement is PowerShellLoweredWhileStatement));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("if ((Value > 0", source, StringComparison.Ordinal);
        Assert.Contains("while (Repeat)", source, StringComparison.Ordinal);
        Assert.Contains("break;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchAssignmentsArePredeclaredAfterDefiniteAssignmentAnalysis()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Choice { param([bool] $Condition) if ($Condition) { [string] $result = 'yes' } else { [string] $result = 'no' }; return $result }",
            TestPath("branch-assignment.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var statements = Assert.Single(result.Lowered.Functions).Statements;
        Assert.IsType<PowerShellLoweredLocalDeclarationStatement>(statements[0]);
        Assert.IsType<PowerShellLoweredIfStatement>(statements[1]);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("string result = default!;", source, StringComparison.Ordinal);
        Assert.Contains("result = (\"yes\" ?? string.Empty);", source, StringComparison.Ordinal);
        Assert.Contains("result = (\"no\" ?? string.Empty);", source, StringComparison.Ordinal);
        Assert.Contains("return result;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ForLoopMutationsAreBoundAndLoweredWithoutEmitterInference()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Sum { param([int] $Count) [int] $total = 0; for ([int] $index = 0; $index -lt $Count; $index++) { $total += $index }; return $total }",
            TestPath("for-loop.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var loop = Assert.IsType<PowerShellBoundForStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements, static statement => statement is PowerShellBoundForStatement));
        Assert.Equal(PowerShellBoundMutationOperator.Assign, loop.Initializer!.Operation);
        Assert.Equal(PowerShellBoundMutationOperator.PostIncrement, loop.Iterator!.Operation);
        var lowered = Assert.IsType<PowerShellLoweredForStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements, static statement => statement is PowerShellLoweredForStatement));
        Assert.True(lowered.DeclareInitializer);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("for (int index = 0; (index < Count); index++)", source, StringComparison.Ordinal);
        Assert.Contains("total = checked((int)(total + index));", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[int[]] $values = 1, 2, 3", "new int[] { 1, 2, 3 }")]
    [InlineData("[string[]] $values = @('one'; 'two')", "new string[] { \"one\", \"two\" }")]
    [InlineData("[string[]] $values = @()", "System.Array.Empty<string>()")]
    public void ArraysCarryContextualElementContractsThroughLowering(string assignment, string expectedSource)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Values {{ {assignment}; return $values }}",
            TestPath("arrays.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var boundAssignment = Assert.IsType<PowerShellBoundAssignmentStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements, static statement => statement is PowerShellBoundAssignmentStatement));
        Assert.IsType<PowerShellBoundArrayExpression>(boundAssignment.Value);
        Assert.Contains(expectedSource, Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[string[]] $Values", "Values ?? global::System.Array.Empty<string>()")]
    [InlineData("[string] $Values", "new[] { Values }")]
    public void ForeachCollectionShapeIsSelectedDuringBinding(string parameter, string expectedEnumerable)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Last {{ param({parameter}) [string] $last = ''; foreach ($value in $Values) {{ $last = $value }}; return $last }}",
            TestPath("foreach.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var loop = Assert.IsType<PowerShellBoundForEachStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements, static statement => statement is PowerShellBoundForEachStatement));
        Assert.Equal(parameter.Contains("[]", StringComparison.Ordinal), !loop.ScalarString);
        Assert.IsType<PowerShellLoweredForEachStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements, static statement => statement is PowerShellLoweredForEachStatement));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("foreach (string value in", source, StringComparison.Ordinal);
        Assert.Contains(expectedEnumerable, source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarSwitchMatchingAndBreakUseLoweredControlFlow()
    {
        var sourceText = "function Get-Choice { param([string] $Value) [string] $result = ''; switch ($Value) { 'one' { $result = '1'; break } default { $result = '0' } }; return $result }";
        var document = PowerShellSourceParser.Parse(sourceText, TestPath("switch.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var bound = Assert.IsType<PowerShellBoundSwitchStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements, static statement => statement is PowerShellBoundSwitchStatement));
        Assert.False(bound.CaseSensitive);
        Assert.IsType<PowerShellLoweredSwitchStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements, static statement => statement is PowerShellLoweredSwitchStatement));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("StringComparison.InvariantCultureIgnoreCase", source, StringComparison.Ordinal);
        Assert.Contains("while (false);", source, StringComparison.Ordinal);
        Assert.Contains("break;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCatchThrowAndRethrowAreLoweredFromExceptionContracts()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Recovered { param([System.InvalidOperationException] $Failure) try { throw $Failure } catch [System.InvalidOperationException] { return 7 } } function Invoke-Rethrow { param([System.Exception] $Failure) try { throw $Failure } catch { throw } }",
            TestPath("try-catch.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var recovered = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-Recovered");
        Assert.Equal(typeof(int), recovered.ReturnType.ClrType);
        var boundTry = Assert.IsType<PowerShellBoundTryStatement>(Assert.Single(recovered.Body.Statements));
        Assert.Equal(typeof(InvalidOperationException), Assert.Single(Assert.Single(boundTry.Catches).ExceptionTypes));
        Assert.IsType<PowerShellBoundThrowStatement>(Assert.Single(boundTry.Body.Statements));
        var recoveredSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Recovered").Source;
        Assert.Contains("throw Failure;", recoveredSource, StringComparison.Ordinal);
        Assert.Contains("catch (global::System.InvalidOperationException)", recoveredSource, StringComparison.Ordinal);
        var rethrowSource = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Invoke_Rethrow").Source;
        Assert.Contains("catch (global::System.Exception)", rethrowSource, StringComparison.Ordinal);
        Assert.Contains("throw;", rethrowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiTypeCatchReportsReachabilityAtItsOwningClause()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Recovered { param([System.Exception] $Failure) try { throw $Failure } catch [System.Exception], [System.InvalidOperationException] { return 7 } }",
            TestPath("multi-type-catch.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        var diagnostic = Assert.Single(result.Bound.Diagnostics, item => item.Code == "PSB2312");
        Assert.Equal(document.DocumentId, diagnostic.Span.DocumentId);
        Assert.Contains("InvalidOperationException", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClrMembersAndExactOverloadsFlowThroughNeutralInteropNodes()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Length { param([string] $Value) return $Value.Length } function Get-Upper { param([string] $Value) return $Value.ToUpperInvariant() } function Get-Absolute { param([double] $Value) return [System.Math]::Abs($Value) }",
            TestPath("clr-interop.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var length = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-Length");
        Assert.IsType<PowerShellBoundClrMemberExpression>(Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(length.Body.Statements)).Expression);
        var upper = Assert.Single(result.Lowered.Functions, static function => function.Symbol.Name == "Get-Upper");
        Assert.IsType<PowerShellLoweredClrInvocationExpression>(Assert.IsType<PowerShellLoweredReturnStatement>(Assert.Single(upper.Statements)).Expression);
        Assert.Contains("(Value ?? string.Empty).Length", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Length").Source, StringComparison.Ordinal);
        Assert.Contains("(Value ?? string.Empty).ToUpperInvariant()", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Upper").Source, StringComparison.Ordinal);
        Assert.Contains("global::System.Math.Abs(Value)", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Absolute").Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClrConstructorAndEnumLiteralAreResolvedBeforeEmission()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Date { return [System.DateTime]::new(2026, 8, 27, 0, 0, 0, 'Utc') }",
            TestPath("clr-constructor.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var invocation = Assert.IsType<PowerShellBoundClrInvocationExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements)).Expression);
        Assert.Equal(PowerShellClrInvocationKind.Constructor, invocation.InvocationKind);
        Assert.Equal(typeof(DateTimeKind), invocation.ParameterTypes[^1]);
        Assert.Contains("(global::System.DateTimeKind)1L", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicTranspilerUsesBoundConditionalPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "condition.ps1");
        try
        {
            File.WriteAllText(path, "function Get-Sign { param([double] $Value) if ($Value -gt 0.0) { return 1.0 }; return -1.0 }");

            var result = new PowerShellTypedCompilationTranspiler().Transpile(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            var semantic = new PowerShellSemanticCompilationPipeline().Compile(new[] { PowerShellSourceParser.ParseFile(path) });
            Assert.Contains(Assert.Single(semantic.Emitted.Methods).Source, result.SourceCode, StringComparison.Ordinal);
            Assert.Contains("if ((Value > 0", result.SourceCode, StringComparison.Ordinal);
            Assert.Contains("return -1", result.SourceCode, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileAndDeclarationOrderDoNotChangeSemanticOrEmissionOrder()
    {
        var first = PowerShellSourceParser.Parse("function Get-Zulu { return 2 }", TestPath("z.ps1"));
        var second = PowerShellSourceParser.Parse("function Get-Alpha { return 1 }", TestPath("a.ps1"));
        var pipeline = new PowerShellSemanticCompilationPipeline();

        var forward = pipeline.Compile(new[] { first, second });
        var reverse = pipeline.Compile(new[] { second, first });

        Assert.Equal(
            forward.Analyzed.Functions.Select(static function => function.Symbol.StableKey),
            reverse.Analyzed.Functions.Select(static function => function.Symbol.StableKey));
        Assert.Equal(
            forward.Emitted.Methods.Select(static method => method.Source),
            reverse.Emitted.Methods.Select(static method => method.Source));
    }

    [Fact]
    public void AssignmentAndAuthoredConversionFlowThroughBoundLocals()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { [long] $value = 42; return $value }",
            TestPath("assignment.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        var local = Assert.Single(function.Locals);
        Assert.Equal(typeof(long), local.Type.ClrType);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("long value = 42;", source, StringComparison.Ordinal);
        Assert.Contains("return value;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DefiniteAssignmentReportsReadBeforeWriteAtTheReadSpan()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { return $value; $value = 42 }",
            TestPath("read-before-write.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        var diagnostic = Assert.Single(result.Analyzed.Diagnostics, static diagnostic => diagnostic.Code == "PSD1001");
        Assert.Contains("read before", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(diagnostic.Span.StartOffset > 0);
    }

    [Fact]
    public void LocalCallGraphPropagatesReturnTypeAndEffects()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Root { return Get-Leaf } function Get-Leaf { $value = 7; return $value }",
            TestPath("calls.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        var root = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-Root");
        Assert.Equal(typeof(int), root.ReturnType.ClrType);
        Assert.True(root.Effects.HasFlag(PowerShellSemanticEffect.Mutation));
        var edge = Assert.Single(result.Analyzed.CallGraph);
        Assert.Equal("Get-Root", edge.Caller.Name);
        Assert.Equal("Get-Leaf", edge.Callee.Name);
        Assert.Contains("return Get_Leaf();", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Root").Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReversingDeclarationsPreservesSemanticAndGeneratedOrder()
    {
        var path = TestPath("declaration-order.ps1");
        var forward = new PowerShellSemanticCompilationPipeline().Compile(new[]
        {
            PowerShellSourceParser.Parse("function Get-Zulu { return 2 } function Get-Alpha { return 1 }", path)
        });
        var reverse = new PowerShellSemanticCompilationPipeline().Compile(new[]
        {
            PowerShellSourceParser.Parse("function Get-Alpha { return 1 } function Get-Zulu { return 2 }", path)
        });

        Assert.Equal(
            forward.Analyzed.Functions.Select(static function => function.Symbol.StableKey),
            reverse.Analyzed.Functions.Select(static function => function.Symbol.StableKey));
        Assert.Equal(
            forward.Emitted.Methods.Select(static method => method.Source),
            reverse.Emitted.Methods.Select(static method => method.Source));
    }

    [Fact]
    public void UnsupportedSyntaxProducesStableBindingDiagnosticWithoutReachingBackend()
    {
        var document = PowerShellSourceParser.Parse("function Get-Value { return \"value: $([datetime]::UtcNow)\" }", TestPath("unsupported.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Methods);
        var diagnostic = Assert.Single(result.Emitted.Diagnostics);
        Assert.Equal("PSB2101", diagnostic.Code);
        Assert.True(diagnostic.Span.StartLine > 0);
    }

    [Fact]
    public void DuplicatePassRegistrationFailsDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellSemanticAnalyzer(new IPowerShellSemanticPass[] { new TestPass("same"), new TestPass("same") }));

        Assert.Contains("registered more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpBackendSurfaceContainsNoParserTypes()
    {
        var backendTypes = typeof(PowerShellBoundCSharpBackend).Assembly.GetTypes()
            .Where(static type => type.Namespace == typeof(PowerShellBoundCSharpBackend).Namespace &&
                                  (type.Name.StartsWith("PowerShellBoundCSharp", StringComparison.Ordinal) ||
                                   type.Name.StartsWith("PowerShellLowered", StringComparison.Ordinal)))
            .ToArray();

        var parserTypeLeak = backendTypes
            .SelectMany(static type => type.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .SelectMany(static constructor => constructor.GetParameters().Select(static parameter => parameter.ParameterType)))
            .FirstOrDefault(static type => type.Namespace?.StartsWith("System.Management.Automation.Language", StringComparison.Ordinal) == true);

        Assert.Null(parserTypeLeak);
    }

    [Fact]
    public void PublicTranspilerUsesTheMigratedLiteralFunctionContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "literal.ps1");
        try
        {
            File.WriteAllText(path, "function Get-Answer { return 42 }");

            var result = new PowerShellTypedCompilationTranspiler().Transpile(path);

            Assert.True(result.Success);
            Assert.Contains("public static int Get_Answer()", result.SourceCode, StringComparison.Ordinal);
            Assert.Contains("return 42;", result.SourceCode, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommentHelpFlowsThroughBoundLoweredAndPublicMetadata()
    {
        var source = "function Get-Helped {\n<#\n.SYNOPSIS\nBound help synopsis.\n.DESCRIPTION\nBound help description.\n.PARAMETER Name\nBound parameter help.\n.EXAMPLE\nGet-Helped -Name Ada\n.NOTES\nBound note.\n.LINK\nhttps://example.com/bound\n.INPUTS\nSystem.String\n.OUTPUTS\nSystem.Int32\n#>\nparam([string] $Name)\nreturn 7\n}";
        var document = PowerShellSourceParser.Parse(source, TestPath("help.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        var help = Assert.Single(result.Analyzed.Functions).Help;
        Assert.NotNull(help);
        Assert.Equal("Bound help synopsis.", help.Synopsis);
        Assert.Equal("Bound parameter help.", help.Parameters["Name"]);
        var emittedHelp = Assert.Single(result.Emitted.Methods).Help;
        Assert.NotNull(emittedHelp);
        Assert.Equal(help.Examples, emittedHelp.Examples);
        Assert.Equal(help.Outputs, emittedHelp.Outputs);
    }

    private static string TestPath(string fileName)
        => Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", fileName);

    private sealed class TestPass : IPowerShellSemanticPass
    {
        internal TestPass(string id) => Id = id;
        public string Id { get; }
        public PowerShellBoundProgram Run(PowerShellBoundProgram program) => program;
    }
}
