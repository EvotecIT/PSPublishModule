using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void NativeProcessLaunchIsClassifiedAndRejectedBeforeTypedEmission()
    {
        var document = PowerShellSourceParser.Parse(
            "function Start-Child { $null = [System.Diagnostics.Process]::Start('pwsh', '-NoProfile'); return }",
            TestPath("native-process.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net8.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        var function = Assert.Single(result.Analyzed.Functions);
        Assert.True(function.Effects.HasFlag(PowerShellSemanticEffect.Process));
        Assert.True(function.Capabilities.HasFlag(PowerShellRequiredCapability.NativeProcess));
        Assert.Empty(result.Lowered.Functions);
        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Lowered.Diagnostics, static diagnostic => diagnostic.Code == "PSL1008");
    }

    [Fact]
    public void StrictClosureVerifierRejectsManagedProcessStartReferences()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PowerShellStrictDependencyClosureVerifier.Verify(new[]
        {
            new PowerShellCompilationArtifactFile { Path = typeof(ProcessRunner).Assembly.Location, Role = "Fixture" }
        }));

        Assert.Contains("native-process launch reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrictClosureVerifierFailsClosedForUnknownExecutableFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Closure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "unknown.exe");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            var result = PowerShellStrictDependencyClosureVerifier.Verify(new[]
            {
                new PowerShellCompilationArtifactFile { Path = path, Role = "Primary", SizeBytes = 4 }
            });

            Assert.False(result.Verified);
            Assert.Contains(result.Limitations, static value => value.Contains("not currently certifiable", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArrayAndForHeaderNodesPropagateNestedProcessEffects()
    {
        var processStart = new PowerShellBoundClrInvocationExpression(
            default,
            typeof(System.Diagnostics.Process),
            nameof(System.Diagnostics.Process.Start),
            PowerShellClrInvocationKind.StaticMethod,
            receiver: null,
            PowerShellClrReceiverBehavior.None,
            Array.Empty<PowerShellBoundExpression>(),
            Array.Empty<Type>(),
            new PowerShellTypeFact(typeof(System.Diagnostics.Process), PowerShellTypeFactProvenance.Inferred, "Test process invocation."));
        var array = new PowerShellBoundArrayExpression(
            default,
            typeof(object[]),
            PowerShellBoundArrayKind.Literal,
            new PowerShellBoundExpression[] { processStart });
        var loop = new PowerShellBoundForStatement(
            default,
            initializer: null,
            condition: processStart,
            iterator: null,
            new PowerShellBoundBlock(default, Array.Empty<PowerShellBoundStatement>()));

        Assert.True(array.Effects.HasFlag(PowerShellSemanticEffect.Process));
        Assert.True(array.Capabilities.HasFlag(PowerShellRequiredCapability.NativeProcess));
        Assert.True(loop.Effects.HasFlag(PowerShellSemanticEffect.Process));
        Assert.True(loop.Capabilities.HasFlag(PowerShellRequiredCapability.NativeProcess));
    }

    [Theory]
    [InlineData('\\', "'\\\\'")]
    [InlineData('\n', "'\\n'")]
    [InlineData('\u0001', "'\\u0001'")]
    public void LoweredCharacterLiteralsUseCanonicalCSharpEscaping(char value, string expected)
    {
        var literal = new PowerShellLoweredLiteralExpression(default, typeof(char), value);

        Assert.Equal(expected, PowerShellBoundCSharpBackend.EmitLiteral(literal));
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void LoweredFloatingPointSpecialValuesUseParseableCanonicalSource(double value, string invariant)
    {
        var literal = new PowerShellLoweredLiteralExpression(default, typeof(double), value);

        var source = PowerShellBoundCSharpBackend.EmitLiteral(literal);

        Assert.Contains("double.Parse", source, StringComparison.Ordinal);
        Assert.Contains('"' + invariant + '"', source, StringComparison.Ordinal);
        Assert.DoesNotContain(invariant + "d", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ForAndForEachVariablesRemainDeclaredAfterTheirLoopScopes()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-LoopValue { param([int[]] $Values) for ([int] $index = 0; $index -lt 1; $index++) { }; " +
            "$index = 2; foreach ($item in $Values) { }; $item = 3; return $item }",
            TestPath("loop-scope.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("int index = default!;", source, StringComparison.Ordinal);
        Assert.Contains("int item = default!;", source, StringComparison.Ordinal);
        Assert.Contains("for (index = 0;", source, StringComparison.Ordinal);
        Assert.Contains("item = __foreachItem_", source, StringComparison.Ordinal);
        Assert.Contains("item = 3;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedForAndForEachVariablesRemainDeclaredAfterTheirLoopScopes()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-NestedLoopValue { param([int[]] $Values) " +
            "for ([int] $outer = 0; $outer -lt 1; $outer++) { " +
            "for ([int] $inner = 0; $inner -lt 1; $inner++) { }; foreach ($item in $Values) { } }; " +
            "$inner = 2; $item = 3; return $item }",
            TestPath("nested-loop-scope.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document });

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("int inner = default!;", source, StringComparison.Ordinal);
        Assert.Contains("int item = default!;", source, StringComparison.Ordinal);
        Assert.Contains("for (inner = 0;", source, StringComparison.Ordinal);
        Assert.Contains("item = __foreachItem_", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticEligibilityOverridesLegacyStructuralDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "SemanticAuthority", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "semantic-authority.ps1");
        try
        {
            File.WriteAllText(path, "function Get-Value { return 5 }");
            var unit = new PowerShellCompilationUnitPlan(
                "Get-Value",
                PowerShellCompilationUnitKind.Function,
                1,
                typeof(object).FullName!,
                Array.Empty<PowerShellCompilationParameter>(),
                new[]
                {
                    new PowerShellCompilationDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        "Synthetic legacy blocker.",
                        path,
                        1,
                        1,
                        "legacy.synthetic")
                });
            var structural = new[]
            {
                new PowerShellCompilationFilePlan(path, Path.GetFileName(path), new[] { unit }, Array.Empty<PowerShellCompilationDiagnostic>())
            };

            var semantic = PowerShellCompilationAnalyzer.ApplySemanticEvidence(
                structural,
                new[] { path },
                root,
                "net8.0",
                PowerShellCompilationCapabilities.StaticRuntimeFacts);

            var analyzed = Assert.Single(Assert.Single(semantic).Units);
            Assert.True(analyzed.IsCompilable);
            Assert.Equal(typeof(int).FullName, analyzed.ReturnType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DocumentIdentityIsRelocationStableAndCaseSensitiveWhenTheFileSystemIs()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "IdentityA");
        var secondRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "IdentityB");
        var first = PowerShellSourceParser.CreateDocumentId(Path.Combine(firstRoot, "Module", "A.ps1"), firstRoot, StringComparison.Ordinal);
        var relocated = PowerShellSourceParser.CreateDocumentId(Path.Combine(secondRoot, "Module", "A.ps1"), secondRoot, StringComparison.Ordinal);
        var differentCase = PowerShellSourceParser.CreateDocumentId(Path.Combine(firstRoot, "Module", "a.ps1"), firstRoot, StringComparison.Ordinal);

        Assert.Equal(first, relocated);
        Assert.NotEqual(first, differentCase);
        Assert.Equal(
            PowerShellSourceParser.CreateDocumentId(Path.Combine(firstRoot, "Module", "A.ps1"), firstRoot, StringComparison.OrdinalIgnoreCase),
            PowerShellSourceParser.CreateDocumentId(Path.Combine(firstRoot, "module", "a.ps1"), firstRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticArraySnapshotsDoNotRetainOrExposeMutableCallerStorage()
    {
        var input = new[] { "first", "second" };
        var snapshot = new PowerShellImmutableArray<string>(input);
        input[0] = "caller-mutated";
        var exposed = snapshot.ToArray();
        exposed[1] = "copy-mutated";

        Assert.Equal("first", snapshot[0]);
        Assert.Equal("second", snapshot[1]);
    }
}
