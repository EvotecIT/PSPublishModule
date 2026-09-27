using System.Management.Automation.Language;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationNativeDependencyTypeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public PowerShellCompilationNativeDependencyTypeTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void DeclaredManagedTypes_UseNativeBindingOnlyAndSurviveFinalExplain(string framework)
    {
        using var fixture = new DependencyFixture();
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.Manifest,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var planner = new PowerShellCompilationDependencyPlanner();
        var dependencies = planner.Analyze(input);
        var graph = planner.AnalyzeGraph(input, targetFramework: framework);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.FullName).ToArray();
        var catalog = PowerShellNativeDependencyTypes.Create(fixture.Manifest, dependencies, graph, input.ModuleRoot);
        Assert.Equal(assemblies, AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.FullName));
        var source = PowerShellSourceParser.Parse(File.ReadAllText(fixture.Source), fixture.Source);
        foreach (var capabilities in new[]
        {
            PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.BinaryModule,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
        })
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, capabilities, catalog).Emitted.Methods);
        var qualified = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule, catalog);
        Assert.Single(qualified.Emitted.Methods);
        Assert.NotNull(Assert.Single(qualified.Analyzed.Functions).NativeFunctionBinding);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid, framework);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, framework);
        Assert.Single(shaped!.Methods);
        var strict = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Strict, framework);
        Assert.False(strict.CanProceed);
    }

    [Fact]
    public void DeclaredCasts_StayRuntimeOwnedAndConstrainedAccessesStayHosted()
    {
        using var fixture = new DependencyFixture();
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.Manifest,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var planner = new PowerShellCompilationDependencyPlanner();
        var catalog = PowerShellNativeDependencyTypes.Create(fixture.Manifest, planner.Analyze(input),
            planner.AnalyzeGraph(input, targetFramework: "net10.0"));
        const string text = """
            function Convert-Dependency {param($Value) [Generic.External.Binary.Dependency.GenericValueSource]$Value}
            function Invoke-Constrained {param($Value) [object]::ReferenceEquals([Generic.External.Binary.Dependency.GenericValueSource]$Value,$null)}
            function Read-Constrained {param($Value) ([Generic.External.Binary.Dependency.GenericValueSource]$Value)[0]}
            """;
        var source = PowerShellSourceParser.Parse(text, fixture.Source);
        var constraint = (TypeConstraintAst)source.SyntaxRoot.Find(
            static node => node is TypeConstraintAst, searchNestedScriptBlocks: true)!;
        foreach (var loaded in new[] { false, true })
        {
            // Only this known test-owned assembly is loaded, to reproduce compiler-host state.
            if (loaded) System.Reflection.Assembly.LoadFrom(fixture.Assembly);
            _output.WriteLine($"Explicit fixture load requested: {loaded}; authored CLR type resolved: {constraint.TypeName.GetReflectionType() is not null}");
            foreach (var framework in new[] { "net10.0", "net472" })
            {
                var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
                    PowerShellCompilationCapabilities.HybridModule, catalog);
                var method = Assert.Single(result.Emitted.Methods);
                Assert.Equal("Convert_Dependency", method.GeneratedName);
                Assert.DoesNotContain("typeof(global::Generic.External.Binary.Dependency", method.Source, StringComparison.Ordinal);
                Assert.Contains(result.Bound.Diagnostics, diagnostic => diagnostic.Code == "PSB2632");
                Assert.Contains(result.Bound.Diagnostics, diagnostic => diagnostic.Code == "PSB2631");
            }
        }
    }

    [Fact]
    public void DeclaredContainersAndOutputMetadata_StayAdvisoryAndNativeOwned()
    {
        using var fixture = new DependencyFixture();
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.Manifest,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var planner = new PowerShellCompilationDependencyPlanner();
        var catalog = PowerShellNativeDependencyTypes.Create(fixture.Manifest, planner.Analyze(input),
            planner.AnalyzeGraph(input, targetFramework: "net10.0"));
        const string text = """
            function Read-DeclaredList {param([Collections.Generic.List[Generic.External.Binary.Dependency.GenericValueSource]]$Value) $Value}
            function Read-DeclaredMap {param([Collections.Generic.Dictionary[string,Collections.Generic.List[Generic.External.Binary.Dependency.GenericValueSource]]]$Value) $Value}
            function Read-DeclaredSet {param([Collections.Generic.HashSet[Generic.External.Binary.Dependency.GenericValueSource]]$Value) $Value}
            function Read-DeclaredArray {param([Generic.External.Binary.Dependency.GenericValueSource[]]$Value) $Value}
            function Read-AdvisoryOutput {[OutputType([Generic.External.Binary.Dependency.GenericValueSource])]param() 7}
            function Read-MissingElement {param([Collections.Generic.List[Undeclared.Missing]]$Value) $Value}
            """;
        var source = PowerShellSourceParser.Parse(text, fixture.Source);
        var authored = (TypeConstraintAst)source.SyntaxRoot.Find(
            static node => node is TypeConstraintAst, searchNestedScriptBlocks: true)!;
        foreach (var loaded in new[] { false, true })
        {
            if (loaded) System.Reflection.Assembly.LoadFrom(fixture.Assembly);
            _output.WriteLine($"Container fixture load requested: {loaded}; authored CLR shape resolved: {authored.TypeName.GetReflectionType() is not null}");
            foreach (var framework in new[] { "net10.0", "net472" })
            {
                var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
                    PowerShellCompilationCapabilities.HybridModule, catalog);
                Assert.Equal(5, result.Emitted.Methods.Length);
                Assert.All(result.Emitted.Methods, method =>
                    Assert.DoesNotContain("typeof(global::Generic.External.Binary.Dependency", method.Source, StringComparison.Ordinal));
                var output = source.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst, true)
                    .OfType<FunctionDefinitionAst>().Single(function => function.Name == "Read-AdvisoryOutput");
                Assert.True(PowerShellOutputTypeSemanticPolicy.TryResolve(output.Body, framework,
                    PowerShellCompilationCapabilities.HybridModule, out var metadata, out _, out _, catalog));
                Assert.Null(metadata.SemanticType);
                Assert.False(Assert.Single(metadata.Declarations).UseClrTypes);
                Assert.Equal("Generic.External.Binary.Dependency.GenericValueSource", metadata.MetadataTypeName);
                foreach (var capabilities in new[]
                {
                    PowerShellCompilationCapabilities.TypedLibrary,
                    PowerShellCompilationCapabilities.BinaryModule,
                    PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
                    PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
                })
                {
                    Assert.False(catalog.Qualifies(authored.TypeName, capabilities));
                    // Already-loaded OutputType metadata has a separate existing advisory
                    // path; disabling this catalog must still reject every container body.
                    var restricted = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, capabilities, catalog);
                    Assert.All(restricted.Emitted.Methods, method => Assert.Equal("Read_AdvisoryOutput", method.GeneratedName));
                }
            }
        }
    }

    [Fact]
    public void DeclaredManagedTypes_RejectChangedBytesAndDoNotInferUndeclaredTypes()
    {
        using var fixture = new DependencyFixture();
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.Manifest,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var planner = new PowerShellCompilationDependencyPlanner();
        var dependencies = planner.Analyze(input);
        var graph = planner.AnalyzeGraph(input, targetFramework: "net10.0");
        var catalog = PowerShellNativeDependencyTypes.Create(fixture.Manifest, dependencies, graph);
        var undeclared = (TypeExpressionAst)PowerShellSourceParser.Parse("[Undeclared.Missing]", fixture.Source).SyntaxRoot
            .Find(static node => node is TypeExpressionAst, searchNestedScriptBlocks: false)!;
        Assert.False(catalog.Qualifies(undeclared.TypeName,
            PowerShellCompilationCapabilities.HybridModule));
        File.AppendAllText(fixture.Assembly, "changed");
        Assert.Throws<InvalidOperationException>(() => PowerShellNativeDependencyTypes.Create(fixture.Manifest, dependencies, graph));
        Assert.Same(PowerShellNativeDependencyTypes.Empty,
            PowerShellNativeDependencyTypes.Create(null, dependencies, graph));
    }

    private sealed class DependencyFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.NativeDependencyTypes", Guid.NewGuid().ToString("N"));
        internal string Manifest => Path.Combine(_root, "Fixture.psd1");
        internal string Source => Path.Combine(_root, "Fixture.psm1");
        internal string Assembly => Path.Combine(_root, "Dependency.dll");
        internal DependencyFixture()
        {
            Directory.CreateDirectory(_root);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Generic.External.Binary.Dependency.dll"), Assembly);
            File.WriteAllText(Manifest, "@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';RequiredAssemblies=@('Dependency.dll');FunctionsToExport=@('Get-DependencyValue')}");
            File.WriteAllText(Source, "function Get-DependencyValue {param([int]$Value) [Generic.External.Binary.Dependency.GenericValueSource]::Resolve($Value)}");
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
