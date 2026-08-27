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

        Assert.Empty(result.Emitted.Diagnostics);
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
    public void UnsupportedSyntaxProducesStableBindingDiagnosticWithoutReachingBackend()
    {
        var document = PowerShellSourceParser.Parse("function Get-Value { return (1 + 2) }", TestPath("unsupported.ps1"));

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

    private static string TestPath(string fileName)
        => Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "BoundPipeline", fileName);

    private sealed class TestPass : IPowerShellSemanticPass
    {
        internal TestPass(string id) => Id = id;
        public string Id { get; }
        public PowerShellBoundProgram Run(PowerShellBoundProgram program) => program;
    }
}
