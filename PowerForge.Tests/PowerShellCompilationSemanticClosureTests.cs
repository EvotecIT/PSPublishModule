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

    [Theory]
    [InlineData("Missing.Dependency")]
    [InlineData("System.CommandLine")]
    public async Task StrictClosureVerifierRejectsMissingTransitiveManagedAssembly(string assemblyName)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "ManagedClosure", Guid.NewGuid().ToString("N"));
        var dependency = Path.Combine(root, "Dependency");
        var consumer = Path.Combine(root, "Consumer");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(consumer);
        try
        {
            File.WriteAllText(Path.Combine(dependency, assemblyName + ".csproj"),
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>{assemblyName}</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(dependency, "Value.cs"),
                $"namespace {assemblyName}; public static class Value {{ public static int Get() => 1; }}");
            File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"),
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Dependency/{assemblyName}.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(consumer, "Consumer.cs"),
                $"public static class Consumer {{ public static int Get() => {assemblyName}.Value.Get(); }}");
            var build = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                    "dotnet",
                    root,
                    new[] { "build", Path.Combine(consumer, "Consumer.csproj"), "-c", "Release", "-o", output, "--nologo", "--verbosity", "quiet" },
                    TimeSpan.FromSeconds(60)));
            Assert.True(build.Succeeded, build.StdErr + Environment.NewLine + build.StdOut);

            var consumerAssembly = Path.Combine(output, "Consumer.dll");
            var exception = Assert.Throws<InvalidOperationException>(() => PowerShellStrictDependencyClosureVerifier.Verify(new[]
            {
                new PowerShellCompilationArtifactFile { Path = consumerAssembly, Role = "Primary" }
            }));

            Assert.Contains($"missing managed assembly '{assemblyName}", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StrictClosureVerifierRejectsDeliveredAssemblyWithWrongIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "ManagedIdentityClosure", Guid.NewGuid().ToString("N"));
        var dependencyV1 = Path.Combine(root, "DependencyV1");
        var dependencyV2 = Path.Combine(root, "DependencyV2");
        var consumer = Path.Combine(root, "Consumer");
        var consumerOutput = Path.Combine(root, "consumer-out");
        var wrongOutput = Path.Combine(root, "wrong-out");
        Directory.CreateDirectory(dependencyV1);
        Directory.CreateDirectory(dependencyV2);
        Directory.CreateDirectory(consumer);
        try
        {
            const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Versioned.Dependency</AssemblyName><AssemblyVersion>{0}</AssemblyVersion></PropertyGroup></Project>";
            File.WriteAllText(Path.Combine(dependencyV1, "Versioned.Dependency.csproj"), string.Format(System.Globalization.CultureInfo.InvariantCulture, project, "1.0.0.0"));
            File.WriteAllText(Path.Combine(dependencyV1, "Value.cs"), "namespace Versioned.Dependency; public static class Value { public static int Get() => 1; }");
            File.WriteAllText(Path.Combine(dependencyV2, "Versioned.Dependency.csproj"), string.Format(System.Globalization.CultureInfo.InvariantCulture, project, "2.0.0.0"));
            File.WriteAllText(Path.Combine(dependencyV2, "Value.cs"), "namespace Versioned.Dependency; public static class Value { public static int Get() => 2; }");
            File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../DependencyV1/Versioned.Dependency.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(consumer, "Consumer.cs"),
                "public static class Consumer { public static int Get() => Versioned.Dependency.Value.Get(); }");

            var consumerBuild = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet", root,
                new[] { "build", Path.Combine(consumer, "Consumer.csproj"), "-c", "Release", "-o", consumerOutput, "--nologo", "--verbosity", "quiet" },
                TimeSpan.FromSeconds(60)));
            Assert.True(consumerBuild.Succeeded, consumerBuild.StdErr + Environment.NewLine + consumerBuild.StdOut);
            var wrongBuild = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet", root,
                new[] { "build", Path.Combine(dependencyV2, "Versioned.Dependency.csproj"), "-c", "Release", "-o", wrongOutput, "--nologo", "--verbosity", "quiet" },
                TimeSpan.FromSeconds(60)));
            Assert.True(wrongBuild.Succeeded, wrongBuild.StdErr + Environment.NewLine + wrongBuild.StdOut);

            var exception = Assert.Throws<InvalidOperationException>(() => PowerShellStrictDependencyClosureVerifier.Verify(new[]
            {
                new PowerShellCompilationArtifactFile { Path = Path.Combine(consumerOutput, "Consumer.dll"), Role = "Primary" },
                new PowerShellCompilationArtifactFile { Path = Path.Combine(wrongOutput, "Versioned.Dependency.dll"), Role = "RuntimeDependency" }
            }));

            Assert.Contains("Versioned.Dependency, Version=1.0.0.0", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exact assembly identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
