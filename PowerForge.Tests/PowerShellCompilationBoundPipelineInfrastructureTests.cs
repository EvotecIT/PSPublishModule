namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
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

    [Theory]
    [InlineData("Get-Help Get-Documented")]
    [InlineData("Get-Help -Name Get-Documented")]
    public void RuntimeFreeGetHelpUsesCanonicalLocalSynopsisMetadata(string invocation)
    {
        var source = $"function Get-Documented {{\n<#\n.SYNOPSIS\nEmbedded synopsis.\n#>\n42\n}}\nfunction Get-Synopsis {{ $Help = {invocation}; return $Help.Synopsis }}";
        var document = PowerShellSourceParser.Parse(source, TestPath("runtime-free-help.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var synopsis = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-Synopsis");
        Assert.Equal(PowerShellDictionaryValueKind.HelpMetadata, Assert.Single(synopsis.Locals).Type.DictionaryValueKind);
        var assignment = Assert.IsType<PowerShellBoundAssignmentStatement>(synopsis.Body.Statements[0]);
        Assert.Equal(PowerShellDictionaryValueKind.HelpMetadata, assignment.Value.Type.DictionaryValueKind);
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(synopsis.Body.Statements[1]).Expression);
        Assert.Equal(typeof(string), member.Type.ClrType);
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookup, member.ReceiverBehavior);
        var generated = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_Synopsis").Source;
        Assert.Contains("{ \"Name\", \"Get-Documented\" }", generated, StringComparison.Ordinal);
        Assert.Contains("{ \"Synopsis\", \"Embedded synopsis.\" }", generated, StringComparison.Ordinal);
        Assert.Contains("TryGetValue(\"Synopsis\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function Get-Documented { 42 }", "$Help = Get-Help Get-Documented; return 42", "PSB2932")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented -Full; return 42", "PSB2930")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented; return $Help.Description", "PSB2933")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented > $null; return 42", "PSB2930")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented; $Help.Clear(); return 42", "PSB2934")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented; $null = $Help.Remove('Synopsis'); return 42", "PSB2934")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented; $Help['Synopsis'] = 'Changed'; return 42", "PSB2934")]
    [InlineData("function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}", "$Help = Get-Help Get-Documented; $Help.Synopsis = 'Changed'; return 42", "PSB2934")]
    public void RuntimeFreeGetHelpRejectsUnboundedHelpSystemShapes(string target, string body, string diagnosticCode)
    {
        var document = PowerShellSourceParser.Parse(
            $"{target} function Get-HelpValue {{ {body} }}",
            TestPath("unsupported-runtime-free-help.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Get_HelpValue");
        Assert.Contains(result.Bound.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    private sealed class TestPass : IPowerShellSemanticPass
    {
        internal TestPass(string id) => Id = id;
        public string Id { get; }
        public PowerShellBoundProgram Run(PowerShellBoundProgram program) => program;
    }
}
